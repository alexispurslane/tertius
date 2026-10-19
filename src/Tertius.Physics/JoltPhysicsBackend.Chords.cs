using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;

namespace Tertius.Physics;

// ---- Instant velocities and chords ----
//
// docs/plans/spin-gravity.md, "The velocity convention". Jolt reads a body's stored velocity as a chord: the step
// v' = v + a·dt, then x' = x + v'·dt, makes v' the displacement over the step divided by the step, which is the velocity
// half a step earlier. A velocity that gameplay gives (a spawn, SetVelocity, a teleport, a jump) or that a hand-off
// carries into another frame is an instant velocity: the velocity at that moment. So physics gives Jolt the chord of the
// coming step, the instant velocity minus the body's acceleration times half a step, and a body under a steady
// acceleration moves exactly as its instant velocity says. The acceleration is the one that the step gives the body
// relative to its home: the home's effective gravity and Jolt's linear damping for a dynamic body, nothing for a
// kinematic one. Jolt's angular damping is the only angular acceleration that a free body has in Jolt, which applies no
// gyroscopic torque.
//
// A velocity relative to a numeric home has two forms. A velocity that gameplay gives is relative to the home's frame
// velocity in the step, as LocalVelocity gives it. The home's frame velocity is a chord too, because the numeric update
// steps it the way that Jolt steps a body (see FrameTree.AdvanceNumericFrames), so the home's instant velocity lies further
// on: half a step of its acceleration for a falling frame, and for an orbit frame whose hull has a sample, the sample's
// instant velocity past its chord (see FrameTree.HomeInstantVelocityOverChord). Only the velocity relative to that instant
// velocity is the body's local instant velocity, whose chord Jolt needs.
//
// The paths that carry a chord on as a chord need no conversion: a move between the systems of one semantic frame and a
// shift of a numeric frame (both homes' velocities are chords, and the body's acceleration in the semantic frame stays
// the same), a growth of a system, and a ghost (a kinematic copy, which Jolt does not accelerate, and which follows the
// chord of its real body or of its frame's step record). A frame's hull that takes the tree's velocity takes the frame's
// instant velocity of its last record, half a step of the hull's acceleration past the chord: a hull is placed from the
// tree only when the tree changes between two steps, and a kinematic hull takes no velocity from the tree at all. Every
// velocity of a body that physics reports is Jolt's: the chord of the last step. The step record of a frame holds both
// velocities (see StepRecordOf).

public sealed partial class JoltPhysicsBackend
{
    // Rigid bodies created since the last step, with the velocities of their spawn: instant velocities, which the start
    // of the next step turns into chords (see ConvertSpawnVelocities).
    private readonly List<BodyRecord> spawnedRigid = new();

    /// <summary>
    /// How much the instant velocity of the origin of <paramref name="home"/> at the end of the step exceeds its frame
    /// velocity, relative to its semantic frame: half a step of a falling frame's acceleration, the sample's instant velocity
    /// past its chord for an orbit frame whose hull has a sample, zero for a semantic frame (see
    /// <see cref="FrameTree.HomeInstantVelocityOverChord"/>). A velocity relative to the home's frame velocity is this much
    /// more than the same velocity relative to the home's instant velocity.
    /// </summary>
    private Double3 HomeInstantVelocityOverChord(FrameHandle home) => frames.HomeInstantVelocityOverChord(home, frames.SemanticOf(home), stepDt);

    /// <summary>
    /// Jolt's linear velocity for the rigid body of <paramref name="record"/> in the system of <paramref name="home"/>,
    /// with its center of mass at <paramref name="center"/>, when its instant velocity relative to the home's frame
    /// velocity in the step is <paramref name="velocity"/>: the chord of the step. Call it only during a step.
    /// </summary>
    private Vector3 LinearChord(BodyRecord record, FrameHandle home, Double3 center, Double3 velocity)
    {
        Contract.Requires(stepDt > 0);
        // The local instant velocity: relative to the home's instant velocity, past its frame velocity.
        Double3 local = velocity - HomeInstantVelocityOverChord(home);
        return (local - ChordAcceleration(record, home, center, local) * (0.5 * stepDt)).ToVector3();
    }

    /// <summary>
    /// The acceleration relative to <paramref name="home"/> that the coming step gives the rigid body of
    /// <paramref name="record"/>, with its center of mass at <paramref name="center"/> and the local instant velocity
    /// <paramref name="local"/>, which turns that velocity into Jolt's chord (see
    /// <see cref="LinearChord(BodyRecord, FrameHandle, Double3, Double3)"/>): the home's effective gravity and Jolt's linear
    /// damping for a dynamic body, nothing for a kinematic one.
    /// </summary>
    private Double3 ChordAcceleration(BodyRecord record, FrameHandle home, Double3 center, Double3 local) =>
        record.Motion == BodyMotion.Dynamic
            ? (Double3)frames.EffectiveGravity(home, center) - record.Material.LinearDamping * local
            : Double3.Zero;

    /// <summary>The chord for a rigid body in the system of its home: see <see cref="LinearChord(BodyRecord, FrameHandle, Double3, Double3)"/>.</summary>
    private Vector3 LinearChord(BodyRecord record, Double3 center, Double3 velocity) => LinearChord(record, record.Frame, center, velocity);

    private static Double3 CenterOfMass(BodyInterface bodies, BodyID id)
    {
        bodies.GetRCenterOfMassPosition(id, out RVector3 center);
        return center.ToDouble3();
    }

    /// <summary>Jolt's angular velocity for the rigid body of <paramref name="record"/> whose instant angular velocity is <paramref name="velocity"/>: the chord of the step.</summary>
    private Vector3 AngularChord(BodyRecord record, Vector3 velocity)
    {
        Contract.Requires(stepDt > 0);
        // Damping slows a spin by k·ω, so the chord is half a step of that faster.
        return record.Motion == BodyMotion.Dynamic ? velocity * (1 + record.Material.AngularDamping * 0.5f * stepDt) : velocity;
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
        return record is { IsHull: false, Motion: BodyMotion.Kinematic } && record.KinematicTargetStep == stepIndex && record.Frame == record.AimedHome;
    }

    /// <summary>
    /// The instant velocity at the end of the step of the primary body of <paramref name="record"/>, whose state Jolt has
    /// at <paramref name="s"/>: relative to the instant velocity of its home there. It is Jolt's chord plus half a step of
    /// the acceleration that the step gave the body: the gravity that physics applied to it, and its damping. A body that
    /// sleeps got none. Call it only after the update of a step.
    /// </summary>
    private Double3 InstantVelocity(BodyRecord record, in BodySnapshot s)
    {
        Contract.Requires(stepDt > 0);
        // A plain kinematic body that a motion intent aimed moves as its step record says (see TryAimedRecord).
        if (TryAimedRecord(record, out MotionSample aimed))
            return aimed.Velocity + Double3.Cross(aimed.AngularVelocity, s.CenterOfMass - s.Pose.Position)
                   - InstantHomeVelocity(record.Frame, frames.SemanticOf(record.Frame));
        double half = 0.5 * stepDt;
        Double3 chord = s.Linear;
        return record.Motion switch
        {
            BodyMotion.Character => chord + (Double3)record.GravityApplied * half,
            BodyMotion.Dynamic when record.World!.Bodies.IsActive(record.Body) =>
                chord + ((Double3)record.GravityApplied - record.Material.LinearDamping * chord) * half,
            _ => chord,
        };
    }

    /// <summary>The instant angular velocity at the end of the step of the primary body of <paramref name="record"/>, whose state Jolt has at <paramref name="s"/>.</summary>
    private Vector3 InstantAngularVelocity(BodyRecord record, in BodySnapshot s)
    {
        Contract.Requires(stepDt > 0);
        if (TryAimedRecord(record, out MotionSample aimed))
            return aimed.AngularVelocity.ToVector3();
        return record.Motion == BodyMotion.Dynamic && record.World!.Bodies.IsActive(record.Body)
            ? s.Angular * (1 - record.Material.AngularDamping * 0.5f * stepDt)
            : s.Angular;
    }

    /// <summary>
    /// Jolt's velocities for the body of <paramref name="record"/> that a hand-off moves into the system of
    /// <paramref name="to"/>, with its center of mass at <paramref name="center"/> there, when its instant velocities
    /// relative to the instant motion of <paramref name="to"/> at the end of the step are <paramref name="linear"/> and
    /// <paramref name="angular"/>: the chords of the next step, and the acceleration relative to <paramref name="to"/> that
    /// turned the linear velocity into its chord. Call it only after the update of a step.
    /// </summary>
    private (Vector3 Linear, Vector3 Angular, Double3 Acceleration) HandedOffVelocities(BodyRecord record, FrameHandle to, Double3 center,
        Double3 linear, Double3 angular)
    {
        Contract.Requires(stepDt > 0);
        double half = 0.5 * stepDt;
        if (record.Character is not null)
        {
            // A character that stood in the step stands in its new frame too, as a rule, where the ground holds it, so its
            // chord is its instant velocity. One that did not stand falls freely there.
            Double3 acceleration = record.Standing ? Double3.Zero : (Double3)frames.EffectiveGravity(to, center);
            return ((linear - acceleration * half).ToVector3(), Vector3.Zero, acceleration);
        }
        // The frame velocity of a numeric home in the next step is its velocity at the end of this step, which lies behind
        // its instant velocity by the home's instant velocity past its chord.
        Double3 velocity = linear + HomeInstantVelocityOverChord(to);
        return (LinearChord(record, to, center, velocity), AngularChord(record, angular.ToVector3()), ChordAcceleration(record, to, center, linear));
    }

    /// <summary>
    /// Gives Jolt the chords of the velocities that the dynamic rigid bodies created since the last step got from their
    /// spawn. Physics creates a body between two steps, when it does not know the length of the next step, so the body
    /// keeps its instant velocities until the start of the step, which converts them here, before anything reads them. A
    /// character makes its chord in its first update instead.
    /// </summary>
    private void ConvertSpawnVelocities()
    {
        foreach (BodyRecord record in spawnedRigid)
        {
            // The body may be gone again, or its entity may have a new body that waits here on its own.
            if (Find(record.Entity) != record || !record.HasPrimary || record.Motion != BodyMotion.Dynamic)
                continue;
            BodyInterface bodies = record.World!.Bodies;
            BodyID id = record.Body;
            bodies.GetLinearAndAngularVelocity(id, out Vector3 linear, out Vector3 angular);
            bodies.SetLinearAndAngularVelocity(id, LinearChord(record, CenterOfMass(bodies, id), linear), AngularChord(record, angular));
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
    /// proper acceleration, half a step further on. A sample has its instant velocity already. Call it only after
    /// <see cref="ComputeProperAccelerations"/>.
    /// </summary>
    private FrameMotion InstantMotionOf(FrameHandle frame)
    {
        MotionSample record = StepRecordOf(frame);
        return new FrameMotion(record.Pose, record.Velocity, record.AngularVelocity);
    }
}
