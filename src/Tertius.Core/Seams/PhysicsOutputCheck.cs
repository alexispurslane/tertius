using System.Runtime.InteropServices;

namespace Tertius;

/// <summary>What the simulation knows about an entity, which a <see cref="PhysicsOutputCheck"/> needs.</summary>
public interface IPhysicsOutputFacts
{
    /// <summary>True while the entity exists.</summary>
    bool IsAlive(Entity entity);

    /// <summary>True when the live entity has a physics body.</summary>
    bool HasBody(Entity entity);

    /// <summary>True when the body of the live entity is a character. The check asks it only for a live entity with a body.</summary>
    bool IsCharacter(Entity entity);

    /// <summary>
    /// The physics home of a live entity with a body, as the step began: the frame whose system held the body, before any
    /// output of the step lands. That is the frame of its transform or a numeric frame below it. Every write to the
    /// transform of an entity with a body comes from physics, so this is the frame that physics names. The check never
    /// asks it for the owner of a frame.
    /// </summary>
    FrameHandle FrameOf(Entity entity);
}

/// <summary>The list of a <see cref="PhysicsStepOutput"/> that holds an output.</summary>
public enum PhysicsOutputKind : byte
{
    BodyState,
    HandOff,
    FrameState,
    Contact,
    CharacterState,
    ContactEnd,
}

/// <summary>An output that only a bug in the backend produces: where it is, and what is wrong with it.</summary>
/// <param name="Kind">The list that holds the output.</param>
/// <param name="Index">The output's index in that list.</param>
/// <param name="Message">What the sync point does with the output and why, for example "ignored the body state of ...".</param>
public readonly record struct PhysicsOutputFault(PhysicsOutputKind Kind, int Index, string Message);

/// <summary>
/// Checks the output of one physics step against what a correct backend keeps, before any of it lands. The sync point and
/// the physics test harness both run it, so that they apply the same rules. See ARCHITECTURE.md, "Output that only a bug
/// produces".
/// </summary>
/// <remarks>
/// <para>A correct backend keeps these rules:</para>
/// <list type="bullet">
/// <item>Entities and frames die in step 5 of the sync point or outside a tick, and physics hears of each death before
/// its next step. An entity keeps its body for its whole life. So every body state, hand-off and contact names live
/// entities with bodies.</item>
/// <item>For the same reason, the frame of every body state and contact is alive, and so is the target of every
/// hand-off. None of them is null.</item>
/// <item>Physics follows the frame tree. So a hand-off leaves the frame that its entity is in as the step began (for
/// the owner of a frame, the frame's parent in the tree), and it enters another frame. The owner of a root frame is in
/// no frame, so it never hands off. Each entity hands off at most once per step.</item>
/// <item>Physics moves a frame only through the hull of a frame with the physics driver, which is a dynamic body in the
/// system of the frame's parent. The simulation makes that a fact: it refuses the physics driver for a frame whose
/// owner's body cannot be dynamic, at spawn and when the driver changes (see <see cref="BodyDesc.FrameHullProblem"/>). So
/// the driver alone tells whether the hull can move. The hull of a frame with another driver is static or kinematic, and
/// physics never hands it off. So the owner of a frame hands off only when its frame has the physics driver.</item>
/// <item>For the same reason, a frame state names a live frame with the physics driver and a parent, whose owner has a
/// body: the hull. A root frame has no parent, so its owner has no hull, and physics never reports its state.</item>
/// <item>The hand-offs of a step, applied in their order, never put a frame under itself or under a frame inside it.
/// Each hull starts the step in the system of its frame's parent, so its frame can only enter a sibling or leave for its
/// parent's parent. It enters a sibling only when its bubble fits in the sibling's, and fitting is a strict order of
/// size, so the entries of one step never close a loop.</item>
/// <item>A contact names two different entities.</item>
/// <item>An ended contact names two different entities too. Destroying a body ends its contacts, and physics hears of
/// the destroy at the sync point before its step, so an ended contact can name an entity that is gone or that has no
/// body. The sync point sends its event only to the entities that are still live.</item>
/// <item>Physics reports the state of each live character once in each step. So a character state names a live entity
/// whose body is a character, and no other character state of the step names the same entity.</item>
/// </list>
/// <para>
/// An output that breaks a rule gets one <see cref="PhysicsOutputFault"/>, however many rules it breaks. The step skips
/// the whole update of every entity that a broken output names (for a frame state, the frame's owner): its body state,
/// its hand-off, the state of the frame that it owns, its contacts and its character state. <see cref="IsSkipped"/> is
/// true for those entities. A broken character state is the one exception: it changes nothing but whether its character
/// stands, so the step skips only that state. <see cref="IsCharacterStateSkipped"/> is true for each broken character
/// state, and for each character state of an entity whose whole update the step skips.
/// </para>
/// </remarks>
public sealed class PhysicsOutputCheck
{
    private readonly List<PhysicsOutputFault> faults = new();
    private readonly HashSet<Entity> skipped = new();
    // Entities with a hand-off in this step, and the indices of the hand-offs that the first pass found broken.
    private readonly HashSet<Entity> handedOff = new();
    private readonly HashSet<int> brokenHandOffs = new();
    // Entities with a character state in this step, and the indices of the character states that the run skips.
    private readonly HashSet<Entity> withCharacterState = new();
    private readonly HashSet<int> skippedCharacterStates = new();
    // The parent of each frame that an earlier hand-off of this step moves, as the hand-offs are applied in order.
    private readonly Dictionary<FrameHandle, FrameHandle> parentsAfter = new();

    /// <summary>
    /// The broken outputs of the last run: body states first, then hand-offs, frame states, contacts, ended contacts, the
    /// hand-offs that the tree refuses, and last the character states. Valid until the next run.
    /// </summary>
    public ReadOnlySpan<PhysicsOutputFault> Faults => CollectionsMarshal.AsSpan(faults);

    /// <summary>True when the last run skips the whole update of <paramref name="entity"/>.</summary>
    public bool IsSkipped(Entity entity) => skipped.Contains(entity);

    /// <summary>
    /// True when the last run skips the character state at <paramref name="index"/> in
    /// <see cref="PhysicsStepOutput.Characters"/>: when the state is broken, or when the run skips the whole update of its
    /// entity.
    /// </summary>
    public bool IsCharacterStateSkipped(int index) => skippedCharacterStates.Contains(index);

    /// <summary>
    /// Checks every output of <paramref name="output"/> against <paramref name="frames"/> and <paramref name="facts"/>,
    /// as they are before any output lands. Sets <see cref="Faults"/>, <see cref="IsSkipped"/> and
    /// <see cref="IsCharacterStateSkipped"/>. Changes nothing else.
    /// Allocates nothing for an output without faults, once warm.
    /// </summary>
    public void Run<TFacts>(PhysicsStepOutput output, FrameTree frames, TFacts facts) where TFacts : IPhysicsOutputFacts
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(frames);
        faults.Clear();
        skipped.Clear();
        handedOff.Clear();
        brokenHandOffs.Clear();
        withCharacterState.Clear();
        skippedCharacterStates.Clear();
        parentsAfter.Clear();

        for (int i = 0; i < output.Bodies.Count; i++)
        {
            BodyState state = output.Bodies[i];
            // The pose and velocities are relative to the reported frame, so they are useless in any other frame.
            if ((BodyFault(state.Entity, facts) ?? FrameFault(state.Frame, frames)) is { } fault)
                Add(PhysicsOutputKind.BodyState, i, $"ignored the body state of {state.Entity}, because {fault}", state.Entity);
        }
        for (int i = 0; i < output.HandOffs.Count; i++)
        {
            HandOff handOff = output.HandOffs[i];
            bool again = !handedOff.Add(handOff.Entity);
            if ((BodyFault(handOff.Entity, facts) ?? FrameFault(handOff.To, frames) ?? MoveFault(handOff, frames, facts) ??
                 (again ? $"{handOff.Entity} has another hand-off in this step" : null)) is { } fault)
            {
                brokenHandOffs.Add(i);
                Add(PhysicsOutputKind.HandOff, i, $"ignored the hand-off of {handOff.Entity}, because {fault}", handOff.Entity);
            }
        }
        for (int i = 0; i < output.Frames.Count; i++)
        {
            FrameHandle frame = output.Frames[i].Frame;
            if (DrivenFrameFault(frame, frames, facts) is { } fault)
                Add(PhysicsOutputKind.FrameState, i, $"ignored the state of {fault}", frame.Entity);
        }
        for (int i = 0; i < output.Contacts.Count; i++)
        {
            ContactStarted contact = output.Contacts[i];
            // The point and normal are relative to the contact's frame.
            if ((BodyFault(contact.A, facts) ?? BodyFault(contact.B, facts) ??
                 (contact.A == contact.B ? $"{contact.A} cannot touch itself" : null) ?? FrameFault(contact.Frame, frames)) is { } fault)
                Add(PhysicsOutputKind.Contact, i, $"ignored the contact of {contact.A} and {contact.B}, because {fault}", contact.A, contact.B);
        }
        for (int i = 0; i < output.ContactsEnded.Count; i++)
        {
            ContactEnded ended = output.ContactsEnded[i];
            if (ended.A == ended.B)
                Add(PhysicsOutputKind.ContactEnd, i, $"ignored the end of the contact of {ended.A} and {ended.B}, because {ended.A} cannot touch itself", ended.A);
        }

        // Last, once every other skip is known, the hand-offs of frame owners in their order, each against the tree as
        // the earlier ones that go ahead leave it. The hand-off of a skipped entity moves nothing, but it is checked too.
        for (int i = 0; i < output.HandOffs.Count; i++)
        {
            HandOff handOff = output.HandOffs[i];
            if (brokenHandOffs.Contains(i) || !frames.TryGetFrame(handOff.Entity, out FrameHandle own))
                continue;
            if (handOff.To == own || IsAncestorAfter(own, handOff.To, frames))
            {
                Add(PhysicsOutputKind.HandOff, i,
                    $"refused the hand-off of {handOff.Entity} from {handOff.From} to {handOff.To}, which would put {own} under itself or its own descendant",
                    handOff.Entity);
                continue;
            }
            if (!skipped.Contains(handOff.Entity))
                parentsAfter[own] = handOff.To;
        }

        // Last, once the skip of every whole update is known, the character states.
        for (int i = 0; i < output.Characters.Count; i++)
        {
            Entity entity = output.Characters[i].Entity;
            bool again = !withCharacterState.Add(entity);
            string? fault = BodyFault(entity, facts) ?? (facts.IsCharacter(entity) ? null : $"{entity} is not a character") ??
                (again ? $"{entity} has another character state in this step" : null);
            if (fault != null)
                faults.Add(new PhysicsOutputFault(PhysicsOutputKind.CharacterState, i, $"ignored the character state of {entity}, because {fault}"));
            if (fault != null || skipped.Contains(entity))
                skippedCharacterStates.Add(i);
        }
    }

    private void Add(PhysicsOutputKind kind, int index, string message, Entity entity, Entity other = default)
    {
        faults.Add(new PhysicsOutputFault(kind, index, message));
        skipped.Add(entity);
        if (!other.IsNull)
            skipped.Add(other);
    }

    /// <summary>Why physics cannot name <paramref name="entity"/> in its output, or null when it can.</summary>
    private static string? BodyFault<TFacts>(Entity entity, TFacts facts) where TFacts : IPhysicsOutputFacts =>
        !facts.IsAlive(entity) ? $"{entity} no longer exists" :
        !facts.HasBody(entity) ? $"{entity} has no body" :
        null;

    /// <summary>Why physics cannot report a state relative to <paramref name="frame"/>, or null when it can.</summary>
    private static string? FrameFault(FrameHandle frame, FrameTree frames) =>
        frame.IsNull ? "it names no frame" :
        !frames.IsAlive(frame) ? $"it names the stale frame {frame}" :
        null;

    /// <summary>
    /// Why the hand-off of a live entity with a body, into a live frame, is not a move that physics makes, or null when
    /// it is.
    /// </summary>
    private static string? MoveFault<TFacts>(in HandOff handOff, FrameTree frames, TFacts facts) where TFacts : IPhysicsOutputFacts
    {
        Entity entity = handOff.Entity;
        bool ownsFrame = frames.TryGetFrame(entity, out FrameHandle own);
        FrameHandle current = ownsFrame ? frames.TreeParent(own) : facts.FrameOf(entity);
        return current.IsNull ? $"{entity} is in no frame, so it leaves none" :
            ownsFrame && frames.Driver(own) != MotionDriver.Physics ? $"physics does not drive {own}, the frame that {entity} owns" :
            handOff.From != current ? $"{entity} is in {current}, not in {handOff.From}" :
            handOff.To == current ? $"it moves {entity} into {current}, which it is in already" :
            null;
    }

    /// <summary>
    /// Why physics cannot report the state of <paramref name="frame"/>, or null when it can. The fault starts with the
    /// frame, because a stale frame has no owner to blame.
    /// </summary>
    private static string? DrivenFrameFault<TFacts>(FrameHandle frame, FrameTree frames, TFacts facts) where TFacts : IPhysicsOutputFacts =>
        !frames.IsAlive(frame) ? $"the stale frame {frame}" :
        frames.Driver(frame) != MotionDriver.Physics ? $"{frame}, because physics does not drive it" :
        frames.TreeParent(frame).IsNull ? $"{frame}, because it is a root frame, so its owner has no hull" :
        BodyFault(frame.Entity, facts) is { } fault ? $"{frame}, because {fault}" :
        null;

    /// <summary>True when <paramref name="ancestor"/> is above <paramref name="frame"/> once the earlier hand-offs are applied.</summary>
    private bool IsAncestorAfter(FrameHandle ancestor, FrameHandle frame, FrameTree frames)
    {
        for (FrameHandle parent = ParentAfter(frame, frames); !parent.IsNull; parent = ParentAfter(parent, frames))
            if (parent == ancestor)
                return true;
        return false;
    }

    private FrameHandle ParentAfter(FrameHandle frame, FrameTree frames) =>
        parentsAfter.TryGetValue(frame, out FrameHandle parent) ? parent : frames.TreeParent(frame);
}
