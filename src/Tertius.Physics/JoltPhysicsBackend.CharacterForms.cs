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
// forms, the intents of a walk, a jump, a motion intent and a spin do not move the rigid form, and a held contact of
// the rigid form ends by the walking form's contact list (see NoteRigidCharacterContacts).

public sealed partial class JoltPhysicsBackend
{
    /// <summary>
    /// The gap in m that a Jolt character keeps from what it touches: Jolt's own default, set on every character. The
    /// capsule of the rigid form is wider by it, so that the two forms reach as far.
    /// </summary>
    private const float CharacterPadding = 0.02f;

    /// <summary>
    /// How far beyond <see cref="CharacterPadding"/> a Jolt character looks for its contacts, in m: Jolt's own default, set
    /// on every character. A character's contact list holds each body within this distance of its padded capsule, asleep
    /// or awake, and it decides the end of a held contact of the character in either form (see
    /// <see cref="NoteRigidCharacterContacts"/>).
    /// </summary>
    private const float CharacterContactReach = 0.1f;

    /// <summary>
    /// Below this effective gravity, in m/s², a character in the walking form takes the rigid form:
    /// <see cref="FrameTree.ZeroGravityThreshold"/>, below which physics never stands a character. So every character that
    /// gravity cannot stand is rigid, and the walking form always has enough gravity to stand.
    /// </summary>
    internal const float RigidBelow = FrameTree.ZeroGravityThreshold;

    /// <summary>
    /// From this effective gravity on, in m/s², a character in the rigid form takes the walking form again, whatever it
    /// touches: twice <see cref="RigidBelow"/>, 0.1 m/s². Between the two numbers, a rigid character that touches nothing
    /// keeps its form, so a floating character whose gravity hovers near the threshold switches once, not in every step.
    /// A rigid character that touches something walks again from <see cref="RigidBelow"/> on (see
    /// <see cref="NextCharacterMotion"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The band is 0.05 m/s² wide, more than the noise of a frame's proper acceleration, which the backend measures from
    /// float velocities: one float step of velocity per step is about 0.03 m/s² at orbital speed (see
    /// <see cref="ComputeProperAccelerations"/>), so a ship that thrusts at about the threshold does not flip its floating
    /// crew between the forms. The effective gravity in a spin frame grows in proportion to the distance from the axis, so
    /// a character that floats near the hub must reach twice its distance from the axis before it walks again.
    /// </para>
    /// <para>
    /// The band holds only a character that touches nothing, because there the two forms move alike: each keeps its
    /// momentum, and the weak gravity and the forces change its velocity in the same way. So in the band, where the
    /// walking form stands, a character that touches something has the walking form, whether it came from below or from
    /// above. A character that lies on a floor when a weak gravity comes stands and walks, as one that spawned in that
    /// gravity does. A ship whose thrust dips below the threshold by noise turns its standing crew rigid for that step,
    /// because nothing can stand them below it, and walks them again in the next step.
    /// </para>
    /// <para>
    /// The band holds against a gravity that changes slowly or by noise. It does not hold a character at the edge of a
    /// gravity region, where the gravity jumps past both numbers: there the character switches once at each crossing,
    /// which needs motion across the edge.
    /// </para>
    /// </remarks>
    internal const float WalkingFrom = 2 * FrameTree.ZeroGravityThreshold;

    // The capsule of the rigid form for each capsule of the walking form, by reference, as the shape cache keeps native
    // shapes. So the characters of one walking capsule share one native rigid capsule, whatever body descriptions they
    // have, and the table holds one entry for each walking capsule, which the shape cache keeps anyway (see RigidForm).
    private readonly Dictionary<CapsuleShapeDesc, CapsuleShapeDesc> rigidCapsules =
        [with(ReferenceEqualityComparer.Instance)];

    // Scratch for SwitchCharacterForms: the characters that change form in this step.
    private readonly List<BodyRecord> formSwitches = [];

    // Scratch for NoteRigidCharacterContacts: the bodies that the contact list of one rigid character holds.
    private readonly List<CollideShapeResult> rigidCharacterContacts = [with(16)];

    // Made with the first query, because the binding fills it from joltc, which Foundation.Init loads.
    private CollideShapeSettings? characterContactSettings;

    /// <summary>
    /// The form that a character in the form <paramref name="current"/> takes in a step that starts with the effective
    /// gravity <paramref name="gravity"/> at it, in m/s²: <see cref="BodyMotion.Dynamic"/> for the rigid form and
    /// <see cref="BodyMotion.Character"/> for the walking form. A walking character turns rigid below
    /// <see cref="RigidBelow"/>. A rigid one walks again from <see cref="WalkingFrom"/> on, or from
    /// <see cref="RigidBelow"/> on when <paramref name="touches"/> is true: it holds a contact that started and has not
    /// ended. A pure rule, which <see cref="SwitchCharacterForms"/> applies.
    /// </summary>
    internal static BodyMotion NextCharacterMotion(BodyMotion current, float gravity, bool touches) => current switch
    {
        BodyMotion.Character when gravity < RigidBelow => BodyMotion.Dynamic,
        BodyMotion.Dynamic when gravity >= WalkingFrom || (touches && gravity >= RigidBelow) => BodyMotion.Character,
        BodyMotion.Static or BodyMotion.Kinematic or BodyMotion.Dynamic or BodyMotion.Character => current
    };

    /// <summary>The mass of a character in kg: its description's, or 1 kg for a description without one, in both forms.</summary>
    private static float CharacterMass(BodyDesc desc) => desc.Mass > 0 ? desc.Mass : 1f;

    /// <summary>
    /// The description of the rigid form of a character with the description <paramref name="desc"/>: a dynamic body with
    /// the character's material and mass (see <see cref="CharacterMass"/>), no damping, as a Jolt character has none, and a
    /// capsule that reaches as far as the walking form does: <see cref="CharacterPadding"/> wider, and its cylinder as much
    /// shorter, so that its ends stay where the walking form's ends are. Each character's record holds its own description,
    /// but every character of one walking capsule gets the same rigid capsule, so one native capsule.
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
        var capsule = (CapsuleShapeDesc)desc.Shape;
        if (!rigidCapsules.TryGetValue(capsule, out CapsuleShapeDesc? rigid))
        {
            // A capsule whose half height is 0 is a sphere (see ShapeCache), so a very short capsule loses its cylinder.
            rigid = new(MathF.Max(0, capsule.HalfHeight - CharacterPadding), capsule.Radius + CharacterPadding);
            rigidCapsules.Add(capsule, rigid);
        }

        return desc with
        {
            Shape = rigid,
            Motion = BodyMotion.Dynamic,
            Mass = CharacterMass(desc),
            LinearDamping = 0,
            AngularDamping = 0
        };
    }

    /// <summary>
    /// The choke point of the forms: gives every character the form that <see cref="NextCharacterMotion"/> asks for, from
    /// the effective gravity at the character at the start of the step and, for a rigid character, whether it touches
    /// something in its system. It runs before the step applies intents and gravity, so that both act on the form that
    /// the character has for the whole step, and before the spawn velocities turn into chords. A switch keeps the
    /// character's frame, pose, velocity, up and contacts (see <see cref="BecomeRigid"/> and <see cref="BecomeWalking"/>).
    /// It visits the members of each system that has a character.
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
                float gravity = frames.EffectiveGravity(world.Frame, PrimaryPosition(member)).ToVector3().Length();
                // The rule reads the touch only of a rigid character from RigidBelow on, and the test costs time in
                // proportion to the pairs of the system, so it runs only then.
                bool touches = member.Motion == BodyMotion.Dynamic && gravity >= RigidBelow &&
                               world.HasStartedPair(member.Body);
                if (NextCharacterMotion(member.Motion, gravity, touches) != member.Motion)
                    formSwitches.Add(member);
            }
        }

        // A switch changes the member list of its system, so the switches come after the visit.
        foreach (BodyRecord record in formSwitches)
            if (record.Motion == BodyMotion.Character)
                BecomeRigid(record);
            else
                BecomeWalking(record);
        formSwitches.Clear();
    }

    /// <summary>
    /// Replaces the Jolt character of a character in the walking form with its rigid form, in the same system, at the same
    /// pose, with the same velocity. The character does not stand in the rigid form, and keeps its up. Its contacts loosen
    /// and the rigid form carries them on at the end of the step (see <see cref="ResolveLooseContacts"/>), and both forms
    /// end a held contact by the same contact list (see <see cref="NoteRigidCharacterContacts"/>), so no contact ends or
    /// starts by the switch. Both forms store the chord of the last step, so the velocity carries over as it is,
    /// except the instant velocity of a spawn since the last step, which turns into a chord as for a spawned rigid body.
    /// </summary>
    private void BecomeRigid(BodyRecord record)
    {
        // Intents and the update of the step have not run yet, so nothing waits for the walking form's next update.
        Contract.Requires(record.IsCharacter && record.Character is not null &&
                          record.PendingVelocityChange == Vector3.Zero && record.PendingJump == 0);
        CharacterVirtual character = record.Character;
        FrameHandle frame = record.Frame;
        var pose = new DPose(character.RPosition.ToDouble3(), character.Rotation.ToDouble());
        Vector3 velocity = character.LinearVelocity;
        RemovePrimary(record);
        record.Motion = BodyMotion.Dynamic;
        record.Standing = false;
        CreatePrimary(record: record, frame: frame, pose: pose, linearVelocity: velocity, angularVelocity: Vector3.Zero,
            upFromGravity: false);
        if (record.InstantVelocityStep == stepIndex)
            spawnedRigid.Add(record);
        Contract.Ensures(record.Motion == BodyMotion.Dynamic && record.Character is null && record.Body.IsValid &&
                         record.Frame == frame);
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
        Contract.Requires(record.IsCharacter && record.Character is null && record.Body.IsValid &&
                          record.Motion == BodyMotion.Dynamic && !spawnedRigid.Contains(record));
        BodySnapshot s = ReadPrimary(record);
        FrameHandle frame = record.Frame;
        RemovePrimary(record);
        record.Motion = BodyMotion.Character;
        CreatePrimary(record: record, frame: frame, pose: s.Pose, linearVelocity: s.Linear,
            angularVelocity: Vector3.Zero, upFromGravity: false);
        Contract.Assert(record.Character is not null && record.HasPrimary);
        record.Character.RefreshContacts(layer: PrimaryLayer(record), physicsSystem: record.World.System);
        Contract.Ensures(record.Motion == BodyMotion.Character && record.Character is not null &&
                         !record.Body.IsValid && record.Frame == frame);
    }

    /// <summary>
    /// Gives each character in the rigid form in <paramref name="world"/> the contact list that its walking form would
    /// have at its pose, after the update of the system: every body within <see cref="CharacterContactReach"/> of the
    /// padded capsule, by the shape query with which a Jolt character finds its contacts. The drain then ends a held
    /// contact of the rigid form as it ends one of the walking form, when the list no longer has the other body (see
    /// <see cref="FrameWorld.NoteRigidCharacterContacts"/>). So a switch of form ends no contact, though the rigid form
    /// alone would end a contact as Jolt does, beyond Jolt's speculative contact distance of 0.02 m. Costs one shape
    /// query for each character in the rigid form in each step in which its system updates, about what the contact
    /// search of a walking character costs.
    /// </summary>
    /// <remarks>
    /// The query takes the walking form's capsule at the rigid body's pose, which is the walking form's pose (see
    /// <see cref="RigidForm"/>), and the settings of Jolt's query for the contacts of a character
    /// (CharacterVirtual::CheckCollision), with one difference. Every character sets enhanced internal edge removal, so
    /// the walking form's query is CollideShapeWithInternalEdgeRemoval, which collides with every edge and then drops the
    /// contacts with internal edges, and the binding does not expose it. That drops contact points but never a body: Jolt
    /// sorts out the contacts with each body on their own, and it always keeps a face contact with a body or, when there
    /// is none, the deepest contact, so every body that the walking form's query meets is in this query's list too. The
    /// list holds bodies, not points, so the two match. It filters no layer: a body that the character cannot touch never
    /// starts a contact with it, so its place in the list changes nothing.
    /// </remarks>
    private void NoteRigidCharacterContacts(FrameWorld world)
    {
        if (world.CharacterCount == world.WalkingCharacterCount)
            return;
        CollideShapeSettings settings = characterContactSettings ??= new()
        {
            MaxSeparationDistance = CharacterPadding + CharacterContactReach,
            BackFaceMode = BackFaceMode.CollideWithBackFaces,
            ActiveEdgeMode = ActiveEdgeMode.CollideOnlyWithActive
        };
        BodyInterface bodies = world.System.BodyInterfaceNoLock;
        foreach (BodyRecord member in world.Members)
        {
            if (!member.IsCharacter || member.Character is not null)
                continue;
            bodies.GetRPosition(member.Body, out RVector3 position);
            RMatrix4x4 transform = Matrix4x4.CreateFromQuaternion(bodies.GetRotation(member.Body));
            transform.Translation = position;
            rigidCharacterContacts.Clear();
            world.System.NarrowPhaseQuery.CollideShape(shape: shapes.Get(member.Desc.Shape), scale: Vector3.One,
                centerOfMassTransform: transform, settings: settings,
                baseOffset: position,
                collectorType: CollisionCollectorType.AllHit, results: rigidCharacterContacts);
            world.NoteRigidCharacterContacts(member.Body, rigidCharacterContacts);
        }
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
            Contract.Invariant(record.Character is null && record.Motion != BodyMotion.Character &&
                               ReferenceEquals(record.Material, record.Desc));
            return;
        }

        Contract.Invariant(record.Motion is BodyMotion.Character or BodyMotion.Dynamic);
        Contract.Invariant(record.Motion == BodyMotion.Character == record.Character is not null);
        Contract.Invariant(record.Motion == BodyMotion.Character || (!record.Standing && record.Body.IsValid));
        if (record.Motion != BodyMotion.Dynamic || record.World is not { } world)
            return;
        Contract.Invariant(world.Bodies.GetMotionType(record.Body) == MotionType.Dynamic);
        BodyLockInterface locks = world.System.BodyLockInterface;
        locks.LockRead(record.Body, out BodyLockRead read);
        bool translationOnly = read.Succeeded && read.Body is { MotionProperties: { IsNotNull: true } motion } &&
                               motion.AllowedDOFs == TranslationOnly;
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
