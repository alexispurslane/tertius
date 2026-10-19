using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Ticket ter-swgm. Jolt keeps rotations as float quaternions, whose length is not exactly 1: the square of it lies between
/// -7.3E-8 and +2.1E-7 away from 1, a new value every step. The backend widens each rotation that leaves Jolt to a
/// double-precision quaternion, and it must normalize it there. The ship's hull rotation becomes the ship frame's local
/// pose, and a local pose that is not unit length puts everything in the parent frame about a meter off, seen from the ship.
/// Each test runs the World on the real backend and checks two things: the rotations in the step outputs that Jolt really
/// sent (which <see cref="FaultInjectingBackend"/> records), and the poses that the World holds after every tick.
/// </summary>
public class UnitRotationTests
{
    private const double Tolerance = 1e-12;

    /// <summary>How far the square of the length of <paramref name="rotation"/> is from 1.</summary>
    private static double Excess(DQuaternion rotation) => Math.Abs(rotation.LengthSquared - 1);

    /// <summary>The largest excess of the rotation that any of the <paramref name="entities"/> has in a body state of the recorded outputs.</summary>
    private static double WorstReportedBodyRotation(FaultInjectingBackend faults, params Entity[] entities) =>
        faults.Outputs.SelectMany(output => output.Bodies).Where(state => entities.Contains(state.Entity)).Max(state => Excess(state.Pose.Rotation));

    [Fact]
    public void EveryRotationOfABodyThatPhysicsReportsIsUnitLength()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f)));
        var cube = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);
        var ball = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 10, LinearDamping: 0, AngularDamping: 0);
        Entity tumbling = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0), Body = cube, AngularVelocity = new Vector3(1.3f, 0.7f, -0.9f) });
        Entity slow = world.Spawn(new SpawnDesc { Frame = space, Pose = At(10), Body = ball, AngularVelocity = new Vector3(0.011f, -0.007f, 0.013f) });

        double worstInTheWorld = 0;
        for (int step = 0; step < 120; step++)
        {
            world.Tick();
            worstInTheWorld = Math.Max(worstInTheWorld, Math.Max(Excess(world.PoseInOwnFrame(tumbling).Rotation), Excess(world.PoseInOwnFrame(slow).Rotation)));
        }

        WorstReportedBodyRotation(faults, tumbling, slow).Should().BeLessThan(Tolerance);
        worstInTheWorld.Should().BeLessThan(Tolerance);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EveryLocalPoseOfAShipFrameThatPhysicsReportsIsUnitLength()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(6.77e6),
            LinearVelocity = new Vector3(0, 7700, 0),
            AngularVelocity = new Vector3(0.0011f, 0.0007f, 0.0013f),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10, 4, 30)), Mass: 1e5f, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);

        double worstInTheWorld = 0;
        for (int step = 0; step < 120; step++)
        {
            world.Tick();
            worstInTheWorld = Math.Max(worstInTheWorld, Math.Max(Excess(world.Frames.LocalPose(ship).Rotation), Excess(world.PoseIn(owner, space).Rotation)));
        }

        double worstFrameState = faults.Outputs.SelectMany(output => output.Frames).Where(state => state.Frame == ship).Max(state => Excess(state.Motion.Pose.Rotation));
        faults.Outputs.SelectMany(output => output.Frames).Count(state => state.Frame == ship).Should().Be(120, "the hull reports a state of the frame in every step");
        worstFrameState.Should().BeLessThan(Tolerance, "the local pose that Jolt sent for the ship frame");
        WorstReportedBodyRotation(faults, owner).Should().BeLessThan(Tolerance);
        worstInTheWorld.Should().BeLessThan(Tolerance, "the local pose of the ship frame and the pose of its owner in the World");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EveryRotationOfACharacterThatPhysicsReportsIsUnitLength()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        // The character stands upright and faces a direction that float rotations cannot hold exactly.
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 0.95, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.9)),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
        });

        double worstInTheWorld = 0;
        for (int step = 0; step < 60; step++)
        {
            world.Tick();
            worstInTheWorld = Math.Max(worstInTheWorld, Excess(world.PoseInOwnFrame(person).Rotation));
        }

        WorstReportedBodyRotation(faults, person).Should().BeLessThan(Tolerance);
        worstInTheWorld.Should().BeLessThan(Tolerance);
        world.Errors.Should().BeEmpty();
    }
}
