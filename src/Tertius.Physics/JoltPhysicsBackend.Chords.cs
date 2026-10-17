using System.Numerics;
using JoltPhysicsSharp;

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
// steps it the way that Jolt steps a body (see FrameTree.AdvanceNumericFrames), so the home's instant velocity is half a
// step of its acceleration further on. Only the velocity relative to that instant velocity is the body's local instant
// velocity, whose chord Jolt needs.
//
// The paths that carry a chord on as a chord need no conversion: a move between the systems of one semantic frame and a
// shift of a numeric frame (both homes' velocities are chords, and the body's acceleration in the semantic frame stays
// the same), a growth of a system, a ghost (a kinematic copy, which Jolt does not accelerate, and which follows the real
// body's chord), and a frame's hull that takes the tree's velocity of a physics-driven frame, which is the hull's own
// chord. Every velocity that physics reports is Jolt's: the chord of the last step.

public sealed partial class JoltPhysicsBackend
{
    // Rigid bodies created since the last step, with the velocities of their spawn: instant velocities, which the start
    // of the next step turns into chords (see ConvertSpawnVelocities).
    private readonly List<BodyRecord> spawnedRigid = new();

    /// <summary>The acceleration of the origin of <paramref name="home"/> relative to its semantic frame in the step: zero for a semantic frame.</summary>
    private Double3 HomeAccelerationOf(FrameHandle home) => frames.HomeAcceleration(home, frames.SemanticOf(home));

    /// <summary>
    /// Jolt's linear velocity for the rigid body of <paramref name="record"/> in the system of <paramref name="home"/>,
    /// with its center of mass at <paramref name="center"/>, when its instant velocity relative to the home's frame
    /// velocity in the step is <paramref name="velocity"/>: the chord of the step. Call it only during a step.
    /// </summary>
    private Vector3 LinearChord(BodyRecord record, FrameHandle home, Double3 center, Double3 velocity)
    {
        Contract.Requires(stepDt > 0);
        double half = 0.5 * stepDt;
        // The local instant velocity: relative to the home's instant velocity, half a step of its acceleration on.
        Double3 local = velocity - HomeAccelerationOf(home) * half;
        if (record.Motion != BodyMotion.Dynamic)
            return local.ToVector3();
        Double3 acceleration = (Double3)frames.EffectiveGravity(home, center) - record.Desc.LinearDamping * local;
        return (local - acceleration * half).ToVector3();
    }

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
        return record.Motion == BodyMotion.Dynamic ? velocity * (1 + record.Desc.AngularDamping * 0.5f * stepDt) : velocity;
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
        double half = 0.5 * stepDt;
        Double3 chord = s.Linear;
        return record.Motion switch
        {
            BodyMotion.Character => chord + (Double3)record.GravityApplied * half,
            BodyMotion.Dynamic when record.World!.Bodies.IsActive(record.Body) =>
                chord + ((Double3)record.GravityApplied - record.Desc.LinearDamping * chord) * half,
            _ => chord,
        };
    }

    /// <summary>The instant angular velocity at the end of the step of the primary body of <paramref name="record"/>, whose state Jolt has at <paramref name="s"/>.</summary>
    private Vector3 InstantAngularVelocity(BodyRecord record, in BodySnapshot s)
    {
        Contract.Requires(stepDt > 0);
        return record.Motion == BodyMotion.Dynamic && record.World!.Bodies.IsActive(record.Body)
            ? s.Angular * (1 - record.Desc.AngularDamping * 0.5f * stepDt)
            : s.Angular;
    }

    /// <summary>
    /// Jolt's velocities for the body of <paramref name="record"/> that a hand-off moves into the system of
    /// <paramref name="to"/>, with its center of mass at <paramref name="center"/> there, when its instant velocities
    /// relative to the instant motion of <paramref name="to"/> at the end of the step are <paramref name="linear"/> and
    /// <paramref name="angular"/>: the chords of the next step. Call it only after the update of a step.
    /// </summary>
    private (Vector3 Linear, Vector3 Angular) HandedOffVelocities(BodyRecord record, FrameHandle to, Double3 center, Double3 linear, Double3 angular)
    {
        Contract.Requires(stepDt > 0);
        double half = 0.5 * stepDt;
        if (record.Character is not null)
        {
            // A character that stood in the step stands in its new frame too, as a rule, where the ground holds it, so its
            // chord is its instant velocity. One that did not stand falls freely there.
            Double3 acceleration = record.Standing ? Double3.Zero : (Double3)frames.EffectiveGravity(to, center);
            return ((linear - acceleration * half).ToVector3(), Vector3.Zero);
        }
        // The frame velocity of a numeric home in the next step is its velocity at the end of this step, which is half a
        // step of its acceleration behind its instant velocity.
        Double3 velocity = linear + HomeAccelerationOf(to) * half;
        return (LinearChord(record, to, center, velocity), AngularChord(record, angular.ToVector3()));
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
    /// <see cref="FrameTree.HomeVelocityAfter"/>), which is a chord, plus half a step of the home's acceleration.
    /// </summary>
    private Double3 InstantHomeVelocity(FrameHandle home, FrameHandle semantic) =>
        frames.HomeVelocityAfter(home, semantic, stepDt) + frames.HomeAcceleration(home, semantic) * (0.5 * stepDt);

    /// <summary>
    /// The motion of <paramref name="frame"/> relative to its current parent at the end of the step, as
    /// <see cref="MotionOf"/> gives it, but with the frame's instant velocity there. Jolt's velocity of the hull of a
    /// physics-driven frame is the chord of the step, so the hull's acceleration over the step, its gravity plus its
    /// proper acceleration, carries it half a step further on. The motion of every other frame has its instant velocity
    /// already (see <see cref="PredictMotion"/>). Call it only after <see cref="ComputeProperAccelerations"/>.
    /// </summary>
    private FrameMotion InstantMotionOf(FrameHandle frame)
    {
        if (!TryHullMotion(frame, out FrameMotion motion))
            return PredictedMotion(frame);
        BodyRecord hull = OwnerRecord(frame)!;
        Double3 acceleration = (Double3)hull.GravityApplied + motion.Pose.Rotation.Rotate((Double3)hull.ProperAcceleration);
        return motion with { Velocity = motion.Velocity + acceleration * (0.5 * stepDt) };
    }
}
