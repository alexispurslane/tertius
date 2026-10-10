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
            h.Intent(box).TeleportPose = target;
            h.Step();

            BodyState state = h[box];
            (state.Pose.Position - target.Position).Length.Should().BeLessThan(1e-6);
            Math.Abs(Dot(state.Pose.Rotation, target.Rotation)).Should().BeGreaterThan(1 - 1e-6);
            state.LinearVelocity.Should().Be(Vector3.Zero);
            state.AngularVelocity.Should().Be(Vector3.Zero);
        }
    }

    [Fact]
    public void TeleportAndKinematicTargetInOneIntentMoveFromTheTeleportPoseToTheTarget()
    {
        var (h, space) = ZeroGravity();
        using (h)
        {
            Entity mover = h.AddBody(space, new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic), Shapes.At(0, 0, 0));
            Entity box = h.AddBody(space, Shapes.Box(0.5f), Shapes.At(0, 10, 0));
            Entity person = h.AddBody(space, new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70), Shapes.At(0, -10, 0));
            h.Step();

            // Each body teleports 20 m away and, in the same intent, gets a target half a meter and 0.1 rad further on.
            var offsets = new Dictionary<Entity, Double3> { [mover] = new(20, 0, 0), [box] = new(0, 20, 0), [person] = new(0, -20, 0) };
            foreach (var (entity, offset) in offsets)
            {
                ref PhysicsIntent intent = ref h.Intent(entity);
                intent.Flags = IntentFlags.Teleport | IntentFlags.KinematicTarget;
                intent.TeleportPose = new DPose(offset, DQuaternion.FromAxisAngle(Double3.UnitY, 0.4));
                intent.Target = new DPose(offset + new Double3(0.5, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5));
            }
            h.Step();

            // Teleport first, then the target: kinematic and dynamic bodies end the step at the target, moving at the
            // speed that carried them there from the teleport pose.
            foreach (Entity entity in new[] { mover, box })
            {
                BodyState state = h[entity];
                DPose target = new(offsets[entity] + new Double3(0.5, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5));
                (state.Pose.Position - target.Position).Length.Should().BeLessThan(1e-4, "{0} ends at its target", entity);
                Math.Abs(Dot(state.Pose.Rotation, target.Rotation)).Should().BeGreaterThan(1 - 1e-5);
                state.LinearVelocity.X.Should().BeApproximately(0.5f / PhysicsHarness.Dt, 1e-3f);
                state.AngularVelocity.Y.Should().BeApproximately(0.1f / PhysicsHarness.Dt, 1e-3f);
            }

            // A target does nothing to a character, so it stays where it teleported to.
            BodyState character = h[person];
            (character.Pose.Position - offsets[person]).Length.Should().BeLessThan(1e-3);
            Math.Abs(Dot(character.Pose.Rotation, DQuaternion.FromAxisAngle(Double3.UnitY, 0.4))).Should().BeGreaterThan(1 - 1e-5);

            // Without a new target, the kinematic body stops there.
            h.Step();
            (h[mover].Pose.Position - (offsets[mover] + new Double3(0.5, 0, 0))).Length.Should().BeLessThan(1e-4);
            h[mover].LinearVelocity.Should().Be(Vector3.Zero);
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
            Vector3.Distance(state.LinearVelocity, new Vector3(3, -1, 0)).Should().BeLessThan(1e-5f);
            Vector3.Distance(state.AngularVelocity, new Vector3(0, 0, 2)).Should().BeLessThan(1e-5f);
            (state.Pose.Position - new Double3(3, -1, 0) * PhysicsHarness.Dt).Length.Should().BeLessThan(1e-6);

            // The velocity persists without further intents.
            h.Step(59);
            (h[box].Pose.Position - new Double3(3, -1, 0)).Length.Should().BeLessThan(1e-4);

            // Each velocity keeps its last value: a new linear velocity leaves the spin alone.
            h.Intent(box).Flags = IntentFlags.SetVelocity;
            h.Intent(box).Velocity = new Vector3(0, 0, 1);
            h.Step();
            Vector3.Distance(h[box].LinearVelocity, new Vector3(0, 0, 1)).Should().BeLessThan(1e-5f);
            Vector3.Distance(h[box].AngularVelocity, new Vector3(0, 0, 2)).Should().BeLessThan(1e-5f);
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
            state.LinearVelocity.X.Should().BeApproximately(2, 5e-5f);
            state.LinearVelocity.Y.Should().BeApproximately(60 * PhysicsHarness.Dt, 5e-5f);
            state.AngularVelocity.Z.Should().BeApproximately(2, 5e-4f);

            // Forces act for one step only.
            h.Step();
            h[ball].LinearVelocity.Y.Should().BeApproximately(60 * PhysicsHarness.Dt, 5e-5f);
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
            h[ball].AngularVelocity.Y.Should().BeApproximately(60 * PhysicsHarness.Dt, 5e-4f);
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
            (state.Pose.Position - target.Position).Length.Should().BeLessThan(1e-5);
            Math.Abs(Dot(state.Pose.Rotation, target.Rotation)).Should().BeGreaterThan(1 - 1e-6);
            state.LinearVelocity.X.Should().BeApproximately(0.5f / PhysicsHarness.Dt, 5e-3f);

            h.Step(10);
            (h[mover].Pose.Position - target.Position).Length.Should().BeLessThan(1e-5);
            h[mover].LinearVelocity.Should().Be(Vector3.Zero);
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
            (h[box].Pose.Position - target.Position).Length.Should().BeLessThan(1e-4);
            Math.Abs(Dot(h[box].Pose.Rotation, target.Rotation)).Should().BeGreaterThan(1 - 1e-5);
            h.Step(5);
            (h[box].Pose.Position - target.Position).Length.Should().BeLessThan(1e-4);
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
            h.Frames.LinearVelocity(ship).Z.Should().BeApproximately(2, 5e-5);
            // An impulse is a jolt: the accelerometer sees Δv / dt for that one step.
            h.Frames.ProperAcceleration(ship).Z.Should().BeApproximately(2 / PhysicsHarness.Dt, 0.05f);
            h.Step();
            h.Frames.ProperAcceleration(ship).Length().Should().BeLessThan(1e-3f);
        }
    }

    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
}
