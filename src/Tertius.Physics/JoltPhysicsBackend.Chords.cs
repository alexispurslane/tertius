using System.Diagnostics;
using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;

namespace Tertius.Physics;

// ---- Instant velocities and chords ----
//
// ARCHITECTURE.md, "Instant velocities and chords". Jolt reads a body's stored velocity as a chord: the step
// v' = v + a·dt, then x' = x + v'·dt, makes v' the displacement over the step divided by the step, which is the velocity
// half a step earlier. A velocity that gameplay gives (a spawn, SetVelocity, a teleport, a jump) or that a hand-off
// carries into another frame is an instant velocity: the velocity at that moment. So physics gives Jolt the chord of the
// coming step, the instant velocity minus the body's acceleration times half a step, and a body under a steady
// acceleration moves exactly as its instant velocity says. The acceleration is the one that the step gives the body
// relative to its home: the home's linear gravity and Jolt's linear damping for a dynamic body, nothing for a
// kinematic one. Jolt's angular damping is the only angular acceleration that a free body has in Jolt, which applies no
// gyroscopic torque.
//
// A velocity relative to a numeric home has two forms. A velocity that gameplay gives is relative to the home's frame
// velocity in the step, as LocalVelocity gives it. The home's frame velocity is a chord too, because the numeric update
// steps it the way that Jolt steps a body (see FrameTree.AdvanceNumericFrames), so the home's instant velocity lies further
// on. Where the step starts, that is half a step of the home's step acceleration (see
// FrameTree.HomeInstantVelocityOverChordAtStepStart), and only the velocity relative to it is the body's local instant
// velocity, whose chord Jolt needs: a velocity that a spawn, gameplay or a hand-off gives a body converts by it, and the
// home's step acceleration then cancels out of the body's motion in the semantic frame. At the end of the step, the
// instant velocity of a hand-off and of a record for the tree lies past the frame velocity by half a step of a falling
// frame's acceleration, and for a following frame by its plan's instant velocity past its chord (see
// FrameTree.HomeInstantVelocityOverChord, InstantHomeVelocity). A body that takes the home's linear gravity is relative
// to the home's instant velocity in Jolt's stepping instead, so physics adds the difference (see
// HomeSteppedVelocityOverPlan).
//
// The paths that carry a chord on as a chord need no conversion: a move between the systems of one semantic frame and a
// shift of a numeric frame (both homes' velocities are chords, and the body's acceleration in the semantic frame stays
// the same), a growth of a system, and a ghost (a kinematic copy, which Jolt does not accelerate, and which follows the
// chord of its real body or of its frame's step record). A frame's hull that takes the tree's velocity takes the frame's
// instant velocity of its last record, half a step of the hull's acceleration past the chord: a hull is placed from the
// tree only when the tree changes between two steps, and a kinematic hull takes no velocity from the tree at all. Every
// velocity of a body that physics reports is Jolt's: the chord of the last step. The step record of a frame holds both
// velocities (see StepRecordOf).
//
// In a turning frame a chord spans the frame's turn over its step, so the chord of a dynamic body is the chord of the
// inertial line through its state, as the frame's axes see it (docs/plans/spin-gravity.md, "The chord rule"): physics gives
// Jolt FrameTurn.ChordBehind of the instant velocity, on the body's semantic place and velocity, with the turn of the step
// that ends at the instant of the velocity, and reads an instant velocity back by its exact inverse, FrameTurn.InstantAtEnd.
// At the start of a step that turn is the frame's turn of the step that ended (V2 of the design), and at its end, for an
// instant velocity at the end of the step or a hand-off, the turn of the step that just ended (V1). The step's two-turn
// update then carries the chord on along the inertial line (see ApplyTurningUpdate). The half step of the damping stays as
// above. The half step of the linear gravity is a change of velocity at the instant, in the axes then, so the chord behind
// takes its field part turned back into the axes of the step that ended, and an instant velocity read at the end of a step
// takes it as it is (see TurningMoment). The angular velocity of a dynamic body that can turn has the same pair,
// FrameTurn.AngularChordBehind and FrameTurn.InstantAngularAtEnd. A kinematic body moves in the frame's coordinates, so it
// takes no turn. A walking character takes the chord rule and the two-turn update in its own update, by what its velocity
// means at the start of the update (see NextCharacterVelocity). One that its ground did not hold takes the inverse at the end
// of the step, and one that did not stand the chord behind at a hand-off, as a dynamic body does. A ghost converts its real
// body's chord by the poses of the frame at both ends of the step (see StepPoses).

public sealed partial class JoltPhysicsBackend
{
    // Rigid bodies created since the last step, with the velocities of their spawn: instant velocities, which the start
    // of the next step turns into chords (see ConvertSpawnVelocities).
    private readonly List<BodyRecord> spawnedRigid = [];

    /// <summary>
    /// How much the instant velocity of the origin of <paramref name="home"/> at the end of the step exceeds its frame
    /// velocity, relative to its semantic frame: half a step of a falling frame's acceleration, the plan's instant velocity
    /// past its chord for a following frame, zero for a semantic frame (see
    /// <see cref="FrameTree.HomeInstantVelocityOverChord"/>). A record that physics releases for the tree adds it, and a
    /// hand-off measures every velocity against the home's frame velocity after the step plus it (see
    /// <see cref="InstantHomeVelocity"/>).
    /// </summary>
    private Double3 HomeInstantVelocityOverChord(FrameHandle home) =>
        frames.HomeInstantVelocityOverChord(home, frames.SemanticOf(home), stepDt);

    /// <summary>
    /// How much the instant velocity of the origin of <paramref name="home"/> where the current step starts exceeds its frame
    /// velocity, relative to its semantic frame: half a step of its step acceleration, zero for a semantic frame (see
    /// <see cref="FrameTree.HomeInstantVelocityOverChordAtStepStart"/>). A velocity that a body gets for the step, relative to
    /// the home's frame velocity, is this much more than the same velocity relative to the home's instant velocity.
    /// </summary>
    private Double3 HomeInstantVelocityOverChordAtStart(FrameHandle home) =>
        frames.HomeInstantVelocityOverChordAtStepStart(home, frames.SemanticOf(home), stepDt);

    /// <summary>
    /// How much the instant velocity of the origin of <paramref name="home"/> at the end of the step in Jolt's stepping
    /// exceeds the one of <see cref="InstantHomeVelocity"/>, relative to its semantic frame. A body that takes the home's
    /// linear gravity in the step, an awake dynamic body or a character that its ground does not hold, also takes minus
    /// the home's step acceleration (see <see cref="FrameTree.LinearGravity"/>), so it moves in the semantic frame as Jolt
    /// moves it in the semantic frame's own system. Its chord plus half a step of its acceleration is then relative to the
    /// home's frame velocity after the step plus half a step of the home's step acceleration (see
    /// <see cref="HomeInstantVelocityOverChordAtStart"/>). Every hand-off, and every record that physics releases, is relative
    /// to the instant velocity of <see cref="InstantHomeVelocity"/>, whose excess over the frame velocity is the plan's for a
    /// following frame (see <see cref="HomeInstantVelocityOverChord"/>). The two differ only for a following frame whose plan
    /// changes its velocity at the start of the step: by half the change when its anchor stops (ticket ter-zsku), and by
    /// the plan's jerk on a smooth plan. Zero for a semantic frame and a falling frame. A body that takes no gravity, such as
    /// a body that sleeps, moves with its home, so its velocity is relative to the home itself.
    /// </summary>
    private Double3 HomeSteppedVelocityOverPlan(FrameHandle home) =>
        HomeInstantVelocityOverChordAtStart(home) - HomeInstantVelocityOverChord(home);

    /// <summary>
    /// A moment at which physics turns an instant velocity of a body in a turning frame into Jolt's chord, or Jolt's chord
    /// back into an instant velocity (docs/plans/spin-gravity.md, "The chord rule"): the turn of the step that ends at that
    /// moment, and the place and the frame velocity then of the origin of the body's home relative to its semantic frame. A
    /// place and a velocity relative to the home, plus these, are the body's semantic place and velocity, on which the rule
    /// acts.
    /// <para>
    /// The half step of the linear gravity in the rule is a change of velocity at the moment, in the frame's axes at the
    /// moment. The chord behind lies in the axes of the step that ended, so the field part of that half step, the linear
    /// gravity plus the home's acceleration, takes the turn back like the rest of the velocity (see
    /// <see cref="ChordBehindChange"/>), as the two-turn update turns the field of the step's start on into the axes at its
    /// end (docs/plans/spin-gravity.md, "The two-turn update"). The instant velocity at the end of a step gains the field at
    /// that moment in the axes then, which is the frame's field there, fixed in its axes, so that half step takes no turn
    /// (see <see cref="InstantAtEndChange"/>).
    /// </para>
    /// </summary>
    /// <param name="Turn">The semantic frame's turn of the step that ends at the moment, and its angular velocity then.</param>
    /// <param name="HomeOffset">The place of the home's origin in the semantic frame at the moment, in m.</param>
    /// <param name="HomeVelocity">The home's frame velocity relative to the semantic frame at the moment, a chord, in m/s.</param>
    /// <param name="HomeAcceleration">The acceleration of the home relative to the semantic frame in the step, in m/s².</param>
    private readonly record struct TurningMoment(
        FrameTurn Turn,
        Double3 HomeOffset,
        Double3 HomeVelocity,
        Double3 HomeAcceleration)
    {
        /// <summary>
        /// How much the chord behind the instant velocity <paramref name="velocity"/>, relative to the home's frame velocity,
        /// of a body at <paramref name="center"/> in the home, under the linear gravity <paramref name="linear"/>, exceeds
        /// that velocity less half a step of the gravity: the turn's part of the chord rule.
        /// </summary>
        public Double3 ChordBehindChange(Double3 center, Double3 velocity, Double3 linear, double dt)
        {
            Double3 semantic = velocity + HomeVelocity - (linear + HomeAcceleration) * (0.5 * dt);
            return Turn.ChordBehind(center + HomeOffset, semantic, dt) - semantic;
        }

        /// <summary>
        /// How much the instant velocity at the moment of a body at <paramref name="center"/> in the home, whose chord
        /// relative to the home's frame velocity is <paramref name="chord"/>, exceeds that chord: the turn's part of the
        /// inverse of the chord rule.
        /// </summary>
        public Double3 InstantAtEndChange(Double3 center, Double3 chord, double dt)
        {
            Double3 semantic = chord + HomeVelocity;
            return Turn.InstantAtEnd(center + HomeOffset, semantic, dt) - semantic;
        }
    }

    /// <summary>
    /// The moment where the step in flight starts, for a body in the system of <paramref name="home"/>, when the home's
    /// semantic frame is a turning frame (see <see cref="FrameTurns.Turns"/>): the turn of the step that ended there, and the
    /// home's place and frame velocity now. Null in a frame that does not turn, which keeps the rule of today to the bit.
    /// </summary>
    private TurningMoment? StepStart(FrameHandle home)
    {
        FrameTurns turns = frames.Turns(home);
        if (!turns.Turns)
            return null;
        FrameHandle semantic = frames.SemanticOf(home);
        return new TurningMoment(Turn: turns.Last, HomeOffset: frames.HomeOffset(home, semantic), HomeVelocity: frames.HomeVelocity(home, semantic),
            HomeAcceleration: frames.HomeAcceleration(home, semantic));
    }

    /// <summary>
    /// The moment where the step in flight ends, for a body in the system of <paramref name="home"/> after the update of the
    /// step, when the home's semantic frame is a turning frame: the turn of the step that just ended, and where the home's
    /// numeric frames will be at the end of it, with their frame velocities of the next step (see
    /// <see cref="FrameTree.HomeOffsetAfter"/>). Null in a frame that does not turn.
    /// </summary>
    private TurningMoment? StepEnd(FrameHandle home)
    {
        FrameTurns turns = frames.Turns(home);
        if (!turns.Turns)
            return null;
        FrameHandle semantic = frames.SemanticOf(home);
        return new TurningMoment(Turn: turns.Step, HomeOffset: frames.HomeOffsetAfter(home, semantic, stepDt),
            HomeVelocity: frames.HomeVelocityAfter(home, semantic, stepDt),
            HomeAcceleration: frames.HomeAcceleration(home, semantic));
    }

    /// <summary>
    /// Jolt's linear velocity for the rigid body of <paramref name="record"/> in the system of <paramref name="home"/>,
    /// with its center of mass at <paramref name="center"/>, when its instant velocity relative to the home's frame
    /// velocity in the step is <paramref name="velocity"/>, at the start of the step: the chord of the step. The body then
    /// moves in the semantic frame as Jolt moves it in the semantic frame's own system: the step acceleration of the home
    /// cancels out (see <see cref="HomeInstantVelocityOverChordAtStart"/>). In a turning frame, whose moment of the velocity
    /// is <paramref name="turning"/>, a dynamic body takes the chord behind its instant velocity on its inertial line (see
    /// <see cref="TurningMoment.ChordBehindChange"/>). Call it only during a step.
    /// </summary>
    private Vector3 LinearChord(BodyRecord record, FrameHandle home, Double3 center, Double3 velocity,
        in TurningMoment? turning)
    {
        Contract.Requires(stepDt > 0);
        // The local instant velocity: relative to the home's instant velocity where the step starts, past its frame velocity.
        Double3 local = velocity - HomeInstantVelocityOverChordAtStart(home);
        Double3 chord = local - ChordAcceleration(record: record, home: home, center: center, local: local) * (0.5 * stepDt);
        if (record.Motion == BodyMotion.Dynamic && turning is { } moment)
            chord += moment.ChordBehindChange(center: center, velocity: velocity, linear: frames.LinearGravity(home, center), dt: stepDt);
        return chord.ToVector3();
    }

    /// <summary>
    /// The acceleration relative to <paramref name="home"/> that the coming step gives the rigid body of
    /// <paramref name="record"/>, with its center of mass at <paramref name="center"/> and the local instant velocity
    /// <paramref name="local"/>, which turns that velocity into Jolt's chord (see
    /// <see cref="LinearChord(BodyRecord, FrameHandle, Double3, Double3, in TurningMoment?)"/>): the home's linear gravity (see
    /// <see cref="FrameTree.LinearGravity"/>) and Jolt's linear damping for a dynamic body, nothing for a kinematic one. The
    /// gravity is the float that <see cref="ApplyGravity()"/> gives Jolt as the body's force.
    /// </summary>
    private Double3 ChordAcceleration(BodyRecord record, FrameHandle home, Double3 center, Double3 local) =>
        record.Motion == BodyMotion.Dynamic
            ? frames.LinearGravity(home, center).ToVector3() - record.Material.LinearDamping * local
            : Double3.Zero;

    /// <summary>
    /// The chord for a rigid body in the system of its home, of an instant velocity at the start of the step in flight: see
    /// <see cref="LinearChord(BodyRecord, FrameHandle, Double3, Double3, in TurningMoment?)"/>.
    /// </summary>
    private Vector3 LinearChordAtStart(BodyRecord record, Double3 center, Double3 velocity) =>
        LinearChord(record: record, home: record.Frame, center: center, velocity: velocity, turning: StepStart(record.Frame));

    private static Double3 CenterOfMass(BodyInterface bodies, BodyID id)
    {
        bodies.GetRCenterOfMassPosition(id, out RVector3 center);
        return center.ToDouble3();
    }

    /// <summary>
    /// Jolt's angular velocity for the rigid body of <paramref name="record"/> whose instant angular velocity is
    /// <paramref name="velocity"/>: the chord of the step. In a turning frame, whose moment of the velocity is
    /// <paramref name="turning"/>, a dynamic body that can turn takes the angular chord behind it on its inertial spin (see
    /// <see cref="FrameTurn.AngularChordBehind"/>). The rigid form of a character cannot turn.
    /// </summary>
    private Vector3 AngularChord(BodyRecord record, Vector3 velocity, in TurningMoment? turning)
    {
        Contract.Requires(stepDt > 0);
        if (record.Motion != BodyMotion.Dynamic)
            return velocity;
        Vector3 chord = turning is { } moment && !record.IsCharacter
            ? moment.Turn.AngularChordBehind(velocity, stepDt).ToVector3()
            : velocity;
        // Damping slows a spin by k·ω, so the chord is half a step of that faster.
        return chord * (1 + record.Material.AngularDamping * 0.5f * stepDt);
    }

    /// <summary>
    /// The step record of the motion intent that aimed the plain kinematic body of <paramref name="record"/> in the current
    /// step, relative to the body's semantic frame, when the body is still in the home where the step started; else false.
    /// Jolt's velocity of the body is the chord of the step, to its float rounding, and its rotation a float, so the body
    /// state and a hand-off of the step take the record's instant velocity and rotation instead (docs/plans/
    /// rails-components.md, "The flow per kind of entity"). After a hand-off the body is in another home, and Jolt holds
    /// the velocity that the hand-off carried, which came from the record. A hull's frame takes its record from the frame
    /// tree (see <see cref="StepRecordOf"/>).
    /// </summary>
    private bool TryAimedRecord(BodyRecord record, out MotionSample aimed)
    {
        aimed = record.AimedRecord;
        return record is { IsHull: false, Motion: BodyMotion.Kinematic } && record.KinematicTargetStep == stepIndex &&
               record.Frame == record.AimedHome;
    }

    /// <summary>
    /// The instant velocity at the end of the step of the primary body of <paramref name="record"/>, whose state Jolt has
    /// at <paramref name="s"/>: relative to the instant velocity of its home there, as <see cref="InstantHomeVelocity"/>
    /// gives it, which every hand-off adds. It is Jolt's chord plus half a step of the acceleration that the step gave the
    /// body: the linear gravity that physics applied to it (the field of <see cref="BodyRecord.FrameAcceleration"/>), and
    /// its damping. A body that sleeps, and a character that its ground held, got none. A body that got the gravity is
    /// relative to the home's instant velocity in Jolt's stepping, so it also takes <see cref="HomeSteppedVelocityOverPlan"/>.
    /// In a turning frame the chord of an awake dynamic body, and of a character that its ground did not hold, spans the
    /// frame's turn of the step, so the exact inverse of the chord rule gives its instant velocity on its inertial line,
    /// with the turn of the step that just ended (see <see cref="TurningMoment.InstantAtEndChange"/>). Call it only after
    /// the update of a step.
    /// </summary>
    private Double3 InstantVelocity(BodyRecord record, in BodySnapshot s)
    {
        Contract.Requires(stepDt > 0 && record.HasPrimary);
        // A plain kinematic body that a motion intent aimed moves as its step record says (see TryAimedRecord).
        if (TryAimedRecord(record, out MotionSample aimed))
            return aimed.Velocity + Double3.Cross(aimed.AngularVelocity, s.CenterOfMass - s.Pose.Position)
                   - InstantHomeVelocity(record.Frame, frames.SemanticOf(record.Frame));
        double half = 0.5 * stepDt;
        Double3 chord = s.Linear;
        switch (record.Motion)
        {
            case BodyMotion.Character:
                Double3 walked = chord + (Double3)record.FrameAcceleration.Field * half;
                if (record.Held)
                    return walked;
                // A character that its ground did not hold took the two-turn update, so its chord spans the frame's turn as
                // a dynamic body's does.
                walked += HomeSteppedVelocityOverPlan(record.Frame);
                if (StepEnd(record.Frame) is { } turning)
                    walked += turning.InstantAtEndChange(s.CenterOfMass, chord, stepDt);
                return walked;
            case BodyMotion.Dynamic when record.World.Bodies.IsActive(record.Body):
                Double3 instant =
                    chord + (record.FrameAcceleration.Field - record.Material.LinearDamping * chord) * half
                          + HomeSteppedVelocityOverPlan(record.Frame);
                if (StepEnd(record.Frame) is { } moment)
                    instant += moment.InstantAtEndChange(s.CenterOfMass, chord, stepDt);
                return instant;
            case BodyMotion.Static or BodyMotion.Kinematic or BodyMotion.Dynamic:
                return chord;
            default:
                throw new UnreachableException($"A body has the motion {record.Motion}, which is not a member.");
        }
    }

    /// <summary>
    /// The instant angular velocity at the end of the step of the primary body of <paramref name="record"/>, whose state
    /// Jolt has at <paramref name="s"/>. In a turning frame an awake dynamic body that can turn takes the exact inverse of the
    /// angular chord rule (see <see cref="FrameTurn.InstantAngularAtEnd"/>).
    /// </summary>
    private Vector3 InstantAngularVelocity(BodyRecord record, in BodySnapshot s)
    {
        Contract.Requires(stepDt > 0 && record.HasPrimary);
        if (TryAimedRecord(record, out MotionSample aimed))
            return aimed.AngularVelocity.ToVector3();
        if (record.Motion != BodyMotion.Dynamic || !record.World.Bodies.IsActive(record.Body))
            return s.Angular;
        Vector3 relative = !record.IsCharacter && StepEnd(record.Frame) is { } moment
            ? moment.Turn.InstantAngularAtEnd(s.Angular, stepDt).ToVector3()
            : s.Angular;
        return relative * (1 - record.Material.AngularDamping * 0.5f * stepDt);
    }

    /// <summary>
    /// Jolt's velocities for the body of <paramref name="record"/> that a hand-off moves into the system of
    /// <paramref name="to"/>, with its center of mass at <paramref name="center"/> there, when its instant velocities
    /// relative to the instant motion of <paramref name="to"/> at the end of the step are <paramref name="linear"/> and
    /// <paramref name="angular"/>: the chords of the next step, and the acceleration relative to <paramref name="to"/> that
    /// turned the linear velocity, relative to the instant velocity of <paramref name="to"/> in Jolt's stepping, into its
    /// chord (see <see cref="HomeSteppedVelocityOverPlan"/>). In a turning frame a dynamic body, and a character that did
    /// not stand, takes the chord behind its instant velocity with the new frame's turn of the step that just ended, which
    /// Jolt's next step, or the character's next update, carries on, and which the next step's two-turn update reads as the
    /// turn of the step that ended. The turn's part of the chord counts in that
    /// acceleration over half a step, so that a hull's step record keeps the instant velocity that the hand-off carried (see
    /// <see cref="TryHullRecord"/>). Call it only after the update of a step.
    /// </summary>
    private (Vector3 Linear, Vector3 Angular, Double3 Acceleration) HandedOffVelocities(BodyRecord record,
        FrameHandle to, Double3 center,
        Double3 linear, Double3 angular)
    {
        Contract.Requires(stepDt > 0);
        double half = 0.5 * stepDt;
        // The frame velocity of a numeric home in the next step is its velocity at the end of this step. The local velocity
        // is relative to the home's instant velocity in Jolt's stepping, half a step of the home's step acceleration past
        // that frame velocity, so the body moves on in the semantic frame as Jolt moves it in the semantic frame's own
        // system (see HomeSteppedVelocityOverPlan).
        Double3 local = linear - HomeSteppedVelocityOverPlan(to);
        // The tree stands where the step started, so it reads the field at the body's place plus the home's move over the
        // step.
        Double3 fieldPlace = center + frames.HomeMoveOver(to, frames.SemanticOf(to), stepDt);
        if (record.Character is not null)
        {
            // A character that stood in the step stands in its new frame too, as a rule, where the ground holds it, so its
            // chord is its instant velocity. One that did not stand falls freely there, under the float linear gravity that
            // its update gives it. In a turning frame its chord is the chord behind its instant velocity on its inertial line,
            // as a dynamic body's, which its next update carries on.
            if (record.Standing)
                return (linear.ToVector3(), Vector3.Zero, Double3.Zero);
            var chordAcceleration = (Double3)frames.LinearGravity(to, fieldPlace).ToVector3();
            if (StepEnd(to) is { } turn)
                chordAcceleration -= turn.ChordBehindChange(center: center, velocity: linear + HomeInstantVelocityOverChord(to),
                    linear: frames.LinearGravity(to, fieldPlace),
                    dt: stepDt) / half;
            return ((local - chordAcceleration * half).ToVector3(), Vector3.Zero, chordAcceleration);
        }

        // The chord plus half a step of the acceleration is the local velocity again (see TryHullRecord). In a turning frame
        // the turn's part of the chord acts on the body's place at the end of the step, where the moment stands, and on its
        // instant velocity relative to the home's frame velocity after the step.
        Double3 acceleration = ChordAcceleration(record: record, home: to, center: fieldPlace, local: local);
        TurningMoment? turning = StepEnd(to);
        if (record.Motion == BodyMotion.Dynamic && turning is { } moment)
            acceleration -= moment.ChordBehindChange(center: center, velocity: linear + HomeInstantVelocityOverChord(to),
                linear: frames.LinearGravity(to, fieldPlace),
                dt: stepDt) / half;
        return ((local - acceleration * half).ToVector3(), AngularChord(record, angular.ToVector3(), turning),
            acceleration);
    }

    /// <summary>
    /// Gives Jolt the chords of the velocities that the dynamic rigid bodies created since the last step got from their
    /// spawn. Physics creates a body between two steps, when it does not know the length of the next step, so the body
    /// keeps its instant velocities until the start of the step, which converts them here, before anything reads them, with
    /// the turn of the step that ended at the spawn in a turning frame. A character makes its chord in its first update
    /// instead.
    /// </summary>
    private void ConvertSpawnVelocities()
    {
        foreach (BodyRecord record in spawnedRigid)
        {
            // The body may be gone again, or its entity may have a new body that waits here on its own.
            if (Find(record.Entity) != record || !record.HasPrimary || record.Motion != BodyMotion.Dynamic)
                continue;
            BodyInterface bodies = record.World.Bodies;
            BodyID id = record.Body;
            bodies.GetLinearAndAngularVelocity(id, out Vector3 linear, out Vector3 angular);
            TurningMoment? turning = StepStart(record.Frame);
            bodies.SetLinearAndAngularVelocity(id,
                LinearChord(record: record, home: record.Frame, center: CenterOfMass(bodies, id), velocity: linear, turning: turning),
                AngularChord(record, angular, turning));
        }

        spawnedRigid.Clear();
    }

    /// <summary>
    /// The instant velocity of the origin of <paramref name="home"/> relative to <paramref name="semantic"/>, the home itself
    /// or an ancestor of it, at the end of the step: the frame velocity after the step (see
    /// <see cref="FrameTree.HomeVelocityAfter"/>), which is a chord, plus the home's instant velocity past its chord (see
    /// <see cref="FrameTree.HomeInstantVelocityOverChord"/>).
    /// </summary>
    private Double3 InstantHomeVelocity(FrameHandle home, FrameHandle semantic) =>
        frames.HomeVelocityAfter(home, semantic, stepDt) + frames.HomeInstantVelocityOverChord(home, semantic, stepDt);

    /// <summary>
    /// The motion of <paramref name="frame"/> relative to its current parent at the end of the step, with the instant velocity
    /// of its step record, which a hand-off carries into the parent (see <see cref="StepRecordOf"/>). Jolt's velocity of a
    /// dynamic hull is the chord of the step, so its record takes the hull's acceleration over the step, its gravity plus its
    /// proper acceleration, half a step further on. A sample has its instant velocity already. The motion holds the record's
    /// acceleration and angular acceleration, by which the hand-off converts a body's angular acceleration (see
    /// <see cref="FrameMotion.AngularAccelerationToParent"/>). Call it only after <see cref="ComputeProperAccelerations"/>.
    /// </summary>
    private FrameMotion InstantMotionOf(FrameHandle frame)
    {
        MotionSample record = StepRecordOf(frame);
        return new(Pose: record.Pose, Velocity: record.Velocity, AngularVelocity: record.AngularVelocity, Acceleration: record.Acceleration,
            AngularAcceleration: record.AngularAcceleration);
    }
}
