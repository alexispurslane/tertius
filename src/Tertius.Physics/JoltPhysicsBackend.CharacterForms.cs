using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

// ---- The two forms of a character ----
//
// A character has one of two forms (ticket ter-1w06). In the walking form it is a Jolt CharacterVirtual, which stands,
// walks, climbs stairs and jumps. In the rigid form it is a dynamic Jolt body with the walking form's capsule, its mass and
// no damping, whose rotation is locked. Jolt's solver handles every contact of the rigid form with friction, as it does for
// any rigid body, so a weightless push conserves momentum and has no strength cap. A CharacterVirtual cannot do that:
// it pushes a dynamic body with at most CharacterMaxStrength, takes no reaction, and its ExtendedUpdate cancels its
// velocity into a steep surface before it moves, which froze a weightless character against a crate.
//
// The record's motion holds the form: BodyMotion.Character for the walking form, BodyMotion.Dynamic for the rigid form. So
// the rigid form takes the path of an ordinary dynamic body everywhere: gravity, intents, hand-offs, moves, shifts,
// growth, ghosts, collision groups, contacts, queries and the outputs. Only a few places know characters: the record's
// input state (up, desired velocity) turns with the axes in a hand-off, the outputs report a character state for both
// forms, and the intents of a walk, a jump, a kinematic target and a spin do not move the rigid form.

public sealed partial class JoltPhysicsBackend
{
    /// <summary>
    /// The gap in m that a Jolt character keeps from what it touches: Jolt's own default, set on every character. The
    /// capsule of the rigid form is wider by it, so that the two forms reach as far.
    /// </summary>
    private const float CharacterPadding = 0.02f;

    /// <summary>
    /// Below this effective gravity, in m/s², a character in the walking form takes the rigid form:
    /// <see cref="FrameTree.ZeroGravityThreshold"/>, below which physics never stands a character. So every character that
    /// gravity cannot stand is rigid, and the walking form always has enough gravity to stand.
    /// </summary>
    internal const float RigidBelow = FrameTree.ZeroGravityThreshold;

    /// <summary>
    /// From this effective gravity on, in m/s², a character in the rigid form takes the walking form again: twice
    /// <see cref="RigidBelow"/>, 0.1 m/s². Between the two numbers a character keeps its form, so a character whose gravity
    /// hovers near the threshold switches once, not in every step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The band is 0.05 m/s² wide, more than the noise of a frame's proper acceleration, which the backend measures from
    /// float velocities: one float step of velocity per step is about 0.03 m/s² at orbital speed (see
    /// <see cref="ComputeProperAccelerations"/>), so a ship that thrusts at about the threshold does not flip its crew
    /// between the forms. The effective gravity in a spin frame grows in proportion to the distance from the axis, so a
    /// character that floats near the hub must reach twice its distance from the axis before it walks again.
    /// </para>
    /// <para>
    /// The number stays small, 1 % of standard gravity, so a character that drifts into a ship's gravity, or onto a
    /// planet, walks again long before it lands. A rigid character in a gravity between the two numbers falls like
    /// loose cargo, which is what a character in so weak a gravity does in the walking form too.
    /// </para>
    /// <para>
    /// The band holds against a gravity that changes slowly or by noise. It does not hold a character at the edge of a
    /// gravity region, where the gravity jumps past both numbers: there the character switches once at each crossing,
    /// which needs motion across the edge.
    /// </para>
    /// </remarks>
    internal const float WalkingFrom = 2 * FrameTree.ZeroGravityThreshold;

    /// <summary>
    /// The form that a character in the form <paramref name="current"/> takes in a step that starts with the effective
    /// gravity <paramref name="gravity"/> at it, in m/s²: <see cref="BodyMotion.Dynamic"/> for the rigid form and
    /// <see cref="BodyMotion.Character"/> for the walking form. A walking character turns rigid below
    /// <see cref="RigidBelow"/>, and a rigid one walks again from <see cref="WalkingFrom"/> on. A pure rule, which
    /// <see cref="SwitchCharacterForms"/> applies.
    /// </summary>
    internal static BodyMotion NextCharacterMotion(BodyMotion current, float gravity) => current switch
    {
        BodyMotion.Character when gravity < RigidBelow => BodyMotion.Dynamic,
        BodyMotion.Dynamic when gravity >= WalkingFrom => BodyMotion.Character,
        _ => current,
    };

    /// <summary>The mass of a character in kg: its description's, or 1 kg for a description without one, in both forms.</summary>
    private static float CharacterMass(BodyDesc desc) => desc.Mass > 0 ? desc.Mass : 1f;

    // The description of the rigid form of each character description, by reference, so that the characters of one
    // description share one native capsule (see RigidForm).
    private readonly Dictionary<BodyDesc, BodyDesc> rigidForms = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The description of the rigid form of a character with the description <paramref name="desc"/>: a dynamic body with
    /// the character's material and mass (see <see cref="CharacterMass"/>), no damping, as a Jolt character has none, and a
    /// capsule that reaches as far as the walking form does: <see cref="CharacterPadding"/> wider, and its cylinder as much
    /// shorter, so that its ends stay where the walking form's ends are.
    /// </summary>
    /// <remarks>
    /// A Jolt character keeps <see cref="CharacterPadding"/> from a wall at its side, but rests its capsule on a floor
    /// (measured on a box floor: the center of a standing capsule of half height 0.6 m and radius 0.3 m is 0.900 m above the
    /// floor, and 0.020 m more than its radius from a wall that it walked into). This capsule touches both, so a switch
    /// moves nothing and keeps every touch. The walking form's own capsule would stand 0.02 m off the wall, at the edge of
    /// Jolt's speculative contact distance, and a capsule only widened by the padding would sink 0.02 m into the floor,
    /// which Jolt pushed 0.015 m up over ten steps.
    /// </remarks>
    private BodyDesc RigidForm(BodyDesc desc)
    {
        if (rigidForms.TryGetValue(desc, out BodyDesc? form))
            return form;
        var capsule = (CapsuleShapeDesc)desc.Shape;
        form = desc with
        {
            // A capsule whose half height is 0 is a sphere (see ShapeCache), so a very short capsule loses its cylinder.
            Shape = new CapsuleShapeDesc(MathF.Max(0, capsule.HalfHeight - CharacterPadding), capsule.Radius + CharacterPadding),
            Motion = BodyMotion.Dynamic,
            Mass = CharacterMass(desc),
            LinearDamping = 0,
            AngularDamping = 0,
        };
        rigidForms.Add(desc, form);
        return form;
    }

    // Scratch for SwitchCharacterForms: the characters that change form in this step.
    private readonly List<BodyRecord> formSwitches = new();

    /// <summary>
    /// The choke point of the forms: gives every character the form that <see cref="NextCharacterMotion"/> asks for, from
    /// the effective gravity at the character at the start of the step. It runs before the step applies intents and
    /// gravity, so that both act on the form that the character has for the whole step, and before the spawn velocities
    /// turn into chords. A switch keeps the character's frame, pose, velocity, up and contacts (see
    /// <see cref="BecomeRigid"/> and <see cref="BecomeWalking"/>). It visits the members of each system that has a character.
    /// </summary>
    private void SwitchCharacterForms()
    {
        formSwitches.Clear();
        foreach (FrameWorld world in worldList)
        {
            if (world.CharacterCount == 0)
                continue;
            foreach (BodyRecord member in world.Members)
            {
                if (!member.IsCharacter)
                    continue;
                float gravity = frames.EffectiveGravity(world.Frame, PrimaryPosition(member)).Length();
                if (NextCharacterMotion(member.Motion, gravity) != member.Motion)
                    formSwitches.Add(member);
            }
        }
        // A switch changes the member list of its system, so the switches come after the visit.
        foreach (BodyRecord record in formSwitches)
        {
            if (record.Motion == BodyMotion.Character)
                BecomeRigid(record);
            else
                BecomeWalking(record);
        }
        formSwitches.Clear();
    }

    /// <summary>
    /// Replaces the Jolt character of a character in the walking form with its rigid form, in the same system, at the same
    /// pose, with the same velocity. The character does not stand in the rigid form, and keeps its up. Its contacts loosen
    /// and the rigid form carries them on at the end of the step (see <see cref="ResolveLooseContacts"/>), so no contact
    /// ends or starts by the switch. Both forms store the chord of the last step, so the velocity carries over as it is,
    /// except the instant velocity of a spawn since the last step, which turns into a chord as for a spawned rigid body.
    /// </summary>
    private void BecomeRigid(BodyRecord record)
    {
        // Intents and the update of the step have not run yet, so nothing waits for the walking form's next update.
        Contract.Requires(record.IsCharacter && record.Character is not null && record.PendingVelocityChange == Vector3.Zero && record.PendingJump == 0);
        CharacterVirtual character = record.Character!;
        FrameHandle frame = record.Frame;
        var pose = new DPose(character.RPosition.ToDouble3(), character.Rotation.ToDouble());
        Vector3 velocity = character.LinearVelocity;
        RemovePrimary(record);
        record.Motion = BodyMotion.Dynamic;
        record.Standing = false;
        CreatePrimary(record, frame, pose, velocity, Vector3.Zero, upFromGravity: false);
        if (record.InstantVelocityStep == stepIndex)
            spawnedRigid.Add(record);
        Contract.Ensures(record.Motion == BodyMotion.Dynamic && record.Character is null && record.Body.IsValid && record.Frame == frame);
    }

    /// <summary>
    /// Replaces the rigid form of a character with a Jolt character in the walking form, in the same system, at the same
    /// pose, with the same velocity and the up that the character had. The new character looks up its contacts at once,
    /// so that its update in this step knows the ground under it. Its contacts carry over as in <see cref="BecomeRigid"/>.
    /// A spawn creates the walking form, and only a step makes a character rigid, after which the step turns any spawn
    /// velocity into a chord. So the rigid form's velocity is always the chord of the last step, as the walking form's is.
    /// </summary>
    private void BecomeWalking(BodyRecord record)
    {
        Contract.Requires(record.IsCharacter && record.Character is null && record.Body.IsValid && record.Motion == BodyMotion.Dynamic && !spawnedRigid.Contains(record));
        BodySnapshot s = ReadPrimary(record);
        FrameHandle frame = record.Frame;
        RemovePrimary(record);
        record.Motion = BodyMotion.Character;
        CreatePrimary(record, frame, s.Pose, s.Linear, Vector3.Zero, upFromGravity: false);
        record.Character!.RefreshContacts(PrimaryLayer(record), record.World!.System);
        Contract.Ensures(record.Motion == BodyMotion.Character && record.Character is not null && !record.Body.IsValid && record.Frame == frame);
    }

    /// <summary>
    /// The invariants of the forms of one record, for <see cref="AssertInvariants"/>: a character has the walking form, a
    /// Jolt character, or the rigid form, a dynamic body that cannot turn and never stands. Any other record has no Jolt
    /// character.
    /// </summary>
    private static void AssertFormInvariants(BodyRecord record)
    {
        if (!record.IsCharacter)
        {
            Contract.Invariant(record.Character is null && record.Motion != BodyMotion.Character && ReferenceEquals(record.Material, record.Desc));
            return;
        }
        Contract.Invariant(record.Motion is BodyMotion.Character or BodyMotion.Dynamic);
        Contract.Invariant((record.Motion == BodyMotion.Character) == (record.Character is not null));
        Contract.Invariant(record.Motion == BodyMotion.Character || (!record.Standing && record.Body.IsValid));
        if (record.Motion != BodyMotion.Dynamic || record.World is not { } world)
            return;
        Contract.Invariant(world.Bodies.GetMotionType(record.Body) == MotionType.Dynamic);
        BodyLockInterface locks = world.System.BodyLockInterface;
        locks.LockRead(record.Body, out BodyLockRead read);
        bool translationOnly = read.Succeeded && read.Body is { MotionProperties: { IsNotNull: true } motion } && motion.AllowedDOFs == TranslationOnly;
        locks.UnlockRead(read);
        Contract.Invariant(translationOnly);
    }

    /// <summary>
    /// The invariants of the character counts of one system, for <see cref="AssertInvariants"/>: it counts its members that
    /// are characters, and those in the walking form.
    /// </summary>
    private static void AssertCharacterCounts(FrameWorld world)
    {
        int characters = 0, walking = 0;
        foreach (BodyRecord member in world.Members)
        {
            if (member.IsCharacter)
                characters++;
            if (member.Character is not null)
                walking++;
        }
        Contract.Invariant(world.CharacterCount == characters && world.WalkingCharacterCount == walking);
    }
}
