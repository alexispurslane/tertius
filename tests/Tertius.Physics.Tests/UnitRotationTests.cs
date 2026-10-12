using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// Ticket ter-swgm. Jolt keeps rotations as float quaternions, whose length is not exactly 1: the square of it lies between
/// -7.3E-8 and +2.1E-7 away from 1, a new value every step. The backend widens each rotation that leaves Jolt to a
/// double-precision quaternion, and it must normalize it there. The ship's hull rotation becomes the ship frame's local
/// pose, and a local pose that is not unit length puts everything in the parent frame about a meter off, seen from the ship.
/// </summary>
public class UnitRotationTests
{
    private const double Tolerance = 1e-12;

    /// <summary>How far the square of the length of <paramref name="rotation"/> is from 1.</summary>
    private static double Excess(DQuaternion rotation) => Math.Abs(rotation.LengthSquared - 1);

    [Fact]
    public void EveryRotationOfABodyThatPhysicsReportsIsUnitLength()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static);
        Entity tumbling = h.AddBody(space, Shapes.Box(0.5f, mass: 10), Shapes.At(0, 0, 0), angularVelocity: new Vector3(1.3f, 0.7f, -0.9f));
        Entity slow = h.AddBody(space, Shapes.Ball(0.5f, mass: 10), Shapes.At(10, 0, 0), angularVelocity: new Vector3(0.011f, -0.007f, 0.013f));

        double worst = 0;
        for (int step = 0; step < 120; step++)
        {
            h.Step();
            worst = Math.Max(worst, Math.Max(Excess(h[tumbling].Pose.Rotation), Excess(h[slow].Pose.Rotation)));
        }

        worst.Should().BeLessThan(Tolerance);
    }

    [Fact]
    public void EveryLocalPoseOfAShipFrameThatPhysicsReportsIsUnitLength()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(6.77e6, 0, 0), FrameBounds.Sphere(100), MotionDriver.Physics,
            new BodyDesc(new BoxShapeDesc(new Vector3(10, 4, 30)), Mass: 1e5f, LinearDamping: 0, AngularDamping: 0),
            velocity: new Vector3(0, 7700, 0), angularVelocity: new Vector3(0.0011f, 0.0007f, 0.0013f));

        double worst = 0;
        for (int step = 0; step < 120; step++)
        {
            h.Step();
            worst = Math.Max(worst, Math.Max(Excess(h.Frames.LocalPose(ship).Rotation), Excess(h[ship.Entity].Pose.Rotation)));
        }

        worst.Should().BeLessThan(Tolerance);
    }

    [Fact]
    public void EveryRotationOfACharacterThatPhysicsReportsIsUnitLength()
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        h.AddBody(ground, Shapes.Floor(50), Shapes.At(0, -0.5, 0));
        // The character stands upright and faces a direction that float rotations cannot hold exactly.
        Entity person = h.AddBody(ground, new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
            new DPose(new Double3(0, 0.95, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.9)));

        double worst = 0;
        for (int step = 0; step < 60; step++)
        {
            h.Step();
            worst = Math.Max(worst, Excess(h[person].Pose.Rotation));
        }

        worst.Should().BeLessThan(Tolerance);
    }
}
