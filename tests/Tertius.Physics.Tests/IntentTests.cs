using System.Numerics;

namespace Tertius.Physics.Tests;

public class IntentTests
{
    private static (PhysicsHarness Harness, FrameHandle Space) ZeroGravity()
    {
        var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        return (h, space);
    }

    [Fact]
    public void TeleportMovesAndStopsTheBody()
    {
        var (h, space) = ZeroGravity();
        using (h)
        {
            Entity box = h.AddBody(space, Shapes.Box(0.5f), Shapes.At(0, 0, 0), velocity: new Vector3(3, 0, 0), angularVelocity: new Vector3(0, 1, 0));
            h.Step(3);
            var target = new DPose(new Double3(100, 5, -7), DQuaternion.FromAxisAngle(Double3.UnitX, 0.5));
            h.Intent(box).Flags = IntentFlags.Teleport;
            h.Intent(box).Target = target;
            h.Step();

            BodyState state = h[box];
            Assert.True((state.Pose.Position - target.Position).Length < 1e-6, state.Pose.Position.ToString());
            Assert.True(Math.Abs(Dot(state.Pose.Rotation, target.Rotation)) > 1 - 1e-6);
            Assert.Equal(Vector3.Zero, state.LinearVelocity);
            Assert.Equal(Vector3.Zero, state.AngularVelocity);
        }
    }

    [Fact]
    public void SetVelocityAndAngularVelocity()
    {
        var (h, space) = ZeroGravity();
        using (h)
        {
            Entity box = h.AddBody(space, Shapes.Box(0.5f), Shapes.At(0, 0, 0));
            h.Intent(box).Flags = IntentFlags.SetVelocity | IntentFlags.SetAngularVelocity;
            h.Intent(box).Velocity = new Vector3(3, -1, 0);
            h.Intent(box).AngularVelocity = new Vector3(0, 0, 2);
            h.Step();
            BodyState state = h[box];
            Assert.True(Vector3.Distance(state.LinearVelocity, new Vector3(3, -1, 0)) < 1e-5f);
            Assert.True(Vector3.Distance(state.AngularVelocity, new Vector3(0, 0, 2)) < 1e-5f);
            Assert.True((state.Pose.Position - new Double3(3, -1, 0) * PhysicsHarness.Dt).Length < 1e-6);

            // The velocity persists without further intents.
            h.Step(59);
            Assert.True((h[box].Pose.Position - new Double3(3, -1, 0)).Length < 1e-4);
        }
    }

    [Fact]
    public void ImpulsesAndForcesChangeVelocityByMass()
    {
        var (h, space) = ZeroGravity();
        using (h)
        {
            Entity ball = h.AddBody(space, Shapes.Ball(0.5f, mass: 2), Shapes.At(0, 0, 0));
            h.Intent(ball).Impulse = new Vector3(4, 0, 0);
            h.Intent(ball).Force = new Vector3(0, 120, 0);
            // A solid sphere: I = 2/5 m r² = 0.2, so 0.4 N·m·s gives 2 rad/s.
            h.Intent(ball).AngularImpulse = new Vector3(0, 0, 0.4f);
            h.Step();
            BodyState state = h[ball];
            Assert.Equal(2, state.LinearVelocity.X, 4);
            Assert.Equal(60 * PhysicsHarness.Dt, state.LinearVelocity.Y, 4);
            Assert.Equal(2, state.AngularVelocity.Z, 3);

            // Forces act for one step only.
            h.Step();
            Assert.Equal(60 * PhysicsHarness.Dt, h[ball].LinearVelocity.Y, 4);
        }
    }

    [Fact]
    public void TorqueSpinsTheBody()
    {
        var (h, space) = ZeroGravity();
        using (h)
        {
            Entity ball = h.AddBody(space, Shapes.Ball(0.5f, mass: 2), Shapes.At(0, 0, 0));
            h.Intent(ball).Torque = new Vector3(0, 12, 0);
            h.Step();
            // α = τ / I = 12 / 0.2 = 60 rad/s², for one step.
            Assert.Equal(60 * PhysicsHarness.Dt, h[ball].AngularVelocity.Y, 3);
        }
    }

    [Fact]
    public void KinematicTargetReachesTheTargetAndStops()
    {
        var (h, space) = ZeroGravity();
        using (h)
        {
            var desc = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic);
            Entity mover = h.AddBody(space, desc, Shapes.At(0, 0, 0));
            var target = new DPose(new Double3(0.5, 0, 0.25), DQuaternion.FromAxisAngle(Double3.UnitY, 0.3));
            h.Intent(mover).Flags = IntentFlags.KinematicTarget;
            h.Intent(mover).Target = target;
            h.Step();
            BodyState state = h[mover];
            Assert.True((state.Pose.Position - target.Position).Length < 1e-5, state.Pose.Position.ToString());
            Assert.True(Math.Abs(Dot(state.Pose.Rotation, target.Rotation)) > 1 - 1e-6);
            Assert.Equal(0.5 / PhysicsHarness.Dt, state.LinearVelocity.X, 2);

            h.Step(10);
            Assert.True((h[mover].Pose.Position - target.Position).Length < 1e-5);
            Assert.Equal(Vector3.Zero, h[mover].LinearVelocity);
        }
    }

    [Fact]
    public void KinematicTargetDrivesDynamicBody()
    {
        var (h, space) = ZeroGravity();
        using (h)
        {
            Entity box = h.AddBody(space, Shapes.Box(0.5f), Shapes.At(0, 0, 0));
            var target = new DPose(new Double3(0.2, 0.1, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.2));
            h.Intent(box).Flags = IntentFlags.KinematicTarget;
            h.Intent(box).Target = target;
            h.Step();
            Assert.True((h[box].Pose.Position - target.Position).Length < 1e-4, h[box].Pose.Position.ToString());
            Assert.True(Math.Abs(Dot(h[box].Pose.Rotation, target.Rotation)) > 1 - 1e-5);
            h.Step(5);
            Assert.True((h[box].Pose.Position - target.Position).Length < 1e-4);
        }
    }

    [Fact]
    public void FrameHullTakesForcesFromItsOwnerIntent()
    {
        var (h, space) = ZeroGravity();
        using (h)
        {
            FrameHandle ship = h.AddFrameWithBody(space, DPose.Identity, FrameBounds.Sphere(30), MotionDriver.Physics,
                new BodyDesc(new SphereShapeDesc(2), Mass: 500, LinearDamping: 0, AngularDamping: 0));
            h.Intent(ship.Entity).Impulse = new Vector3(0, 0, 1000);
            h.Step();
            Assert.Equal(2, h.Frames.LinearVelocity(ship).Z, 4);
            // An impulse is a jolt: the accelerometer sees Δv / dt for that one step.
            Assert.Equal(2 / PhysicsHarness.Dt, h.Frames.ProperAcceleration(ship).Z, 1);
            h.Step();
            Assert.True(h.Frames.ProperAcceleration(ship).Length() < 1e-3f);
        }
    }

    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
}
