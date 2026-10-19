using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// What each physics intent does to a body, on the World and the real Jolt backend. A component writes an intent through
/// <see cref="BodyHandle"/> during a tick, and physics applies it in the step of the next tick. So every test sends a
/// command with <see cref="Send"/>, which ticks once, and ticks once more to see what the command did.
/// </summary>
public class IntentTests
{
    /// <summary>A world with a root frame that has no gravity.</summary>
    private static (World World, FrameHandle Space) ZeroGravity()
    {
        World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        return (world, space);
    }

    /// <summary>A dynamic cube with sides of <c>2 · half</c> m that nothing slows down.</summary>
    private static BodyDesc Box(float half, float mass = 1f) =>
        new(new BoxShapeDesc(new Vector3(half)), BodyMotion.Dynamic, Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// Gives <paramref name="command"/> to the pilot and ticks once. The tick writes the command into the body's intent, so
    /// the step of that tick does not know it yet. The step of the next tick applies it.
    /// </summary>
    private static void Send(World world, Pilot pilot, BodyCommand command)
    {
        pilot.Once = command;
        world.Tick();
    }

    private static Entity Spawn(World world, FrameHandle frame, BodyDesc body, DPose pose, Pilot pilot, Double3 velocity = default, Vector3 angularVelocity = default) =>
        world.Spawn(new SpawnDesc { Frame = frame, Pose = pose, LinearVelocity = velocity, AngularVelocity = angularVelocity, Body = body, Components = [pilot] });

    [Fact]
    public void TeleportMovesAndStopsTheBody()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var pilot = new Pilot();
            Entity box = Spawn(world, space, Box(0.5f), At(0, 0, 0), pilot, velocity: new Double3(3, 0, 0), angularVelocity: new Vector3(0, 1, 0));
            Tick(world, 3);
            var target = new DPose(new Double3(100, 5, -7), DQuaternion.FromAxisAngle(Double3.UnitX, 0.5));
            Send(world, pilot, body => body.Teleport(target));
            world.Tick();

            DPose pose = world.PoseInOwnFrame(box);
            (pose.Position - target.Position).Length.Should().BeLessThan(1e-6);
            Math.Abs(Dot(pose.Rotation, target.Rotation)).Should().BeGreaterThan(1 - 1e-6);
            world.VelocityInOwnFrame(box).Should().Be(Vector3.Zero);
            world.AngularVelocityInOwnFrame(box).Should().Be(Vector3.Zero);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void TeleportAndKinematicTargetInOneIntentMoveFromTheTeleportPoseToTheTarget()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var moverPilot = new Pilot();
            var boxPilot = new Pilot();
            var personPilot = new Pilot();
            Entity mover = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic), At(0, 0, 0), moverPilot);
            Entity box = Spawn(world, space, Box(0.5f), At(0, 10, 0), boxPilot);
            Entity person = Spawn(world, space, new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70), At(0, -10, 0), personPilot);
            world.Tick();

            // Each body teleports 20 m away and, in the same intent, gets a target half a meter and 0.1 rad further on.
            var offsets = new Dictionary<Entity, Double3> { [mover] = new(20, 0, 0), [box] = new(0, 20, 0), [person] = new(0, -20, 0) };
            foreach (var (entity, pilot) in new[] { (mover, moverPilot), (box, boxPilot), (person, personPilot) })
            {
                Double3 offset = offsets[entity];
                pilot.Once = body =>
                {
                    body.Teleport(new DPose(offset, DQuaternion.FromAxisAngle(Double3.UnitY, 0.4)));
                    body.MoveKinematic(new DPose(offset + new Double3(0.5, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5)));
                };
            }
            world.Tick();
            world.Tick();

            // Teleport first, then the target: kinematic and dynamic bodies end the step at the target, moving at the
            // speed that carried them there from the teleport pose.
            foreach (Entity entity in new[] { mover, box })
            {
                DPose pose = world.PoseInOwnFrame(entity);
                DPose target = new(offsets[entity] + new Double3(0.5, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5));
                (pose.Position - target.Position).Length.Should().BeLessThan(1e-4, "{0} ends at its target", entity);
                Math.Abs(Dot(pose.Rotation, target.Rotation)).Should().BeGreaterThan(1 - 1e-5);
                world.VelocityInOwnFrame(entity).X.Should().BeApproximately(0.5f / world.DeltaTime, 1e-3f);
                world.AngularVelocityInOwnFrame(entity).Y.Should().BeApproximately(0.1f / world.DeltaTime, 1e-3f);
            }

            // A target does nothing to a character, so it stays where it teleported to.
            DPose character = world.PoseInOwnFrame(person);
            (character.Position - offsets[person]).Length.Should().BeLessThan(1e-3);
            Math.Abs(Dot(character.Rotation, DQuaternion.FromAxisAngle(Double3.UnitY, 0.4))).Should().BeGreaterThan(1 - 1e-5);

            // Without a new target, the kinematic body stops there.
            world.Tick();
            (world.PoseInOwnFrame(mover).Position - (offsets[mover] + new Double3(0.5, 0, 0))).Length.Should().BeLessThan(1e-4);
            world.VelocityInOwnFrame(mover).Should().Be(Vector3.Zero);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void SetVelocityAndAngularVelocity()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var pilot = new Pilot();
            Entity box = Spawn(world, space, Box(0.5f), At(0, 0, 0), pilot);
            Send(world, pilot, body =>
            {
                body.SetVelocity(new Double3(3, -1, 0));
                body.SetAngularVelocity(new Vector3(0, 0, 2));
            });
            world.Tick();
            Vector3.Distance(world.VelocityInOwnFrame(box), new Vector3(3, -1, 0)).Should().BeLessThan(1e-5f);
            Vector3.Distance(world.AngularVelocityInOwnFrame(box), new Vector3(0, 0, 2)).Should().BeLessThan(1e-5f);
            (world.PoseInOwnFrame(box).Position - new Double3(3, -1, 0) * world.DeltaTime).Length.Should().BeLessThan(1e-6);

            // The velocity persists without further intents.
            Tick(world, 59);
            (world.PoseInOwnFrame(box).Position - new Double3(3, -1, 0)).Length.Should().BeLessThan(1e-4);

            // Each velocity keeps its last value: a new linear velocity leaves the spin alone.
            Send(world, pilot, body => body.SetVelocity(new Double3(0, 0, 1)));
            world.Tick();
            Vector3.Distance(world.VelocityInOwnFrame(box), new Vector3(0, 0, 1)).Should().BeLessThan(1e-5f);
            Vector3.Distance(world.AngularVelocityInOwnFrame(box), new Vector3(0, 0, 2)).Should().BeLessThan(1e-5f);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ImpulsesAndForcesChangeVelocityByMass()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var pilot = new Pilot();
            Entity ball = Spawn(world, space, Ball(0.5f, mass: 2), At(0, 0, 0), pilot);
            Send(world, pilot, body =>
            {
                body.AddImpulse(new Vector3(4, 0, 0));
                body.AddForce(new Vector3(0, 120, 0));
                // A solid sphere: I = 2/5 m r² = 0.2, so 0.4 N·m·s gives 2 rad/s.
                body.AddAngularImpulse(new Vector3(0, 0, 0.4f));
            });
            world.Tick();
            Vector3 velocity = world.VelocityInOwnFrame(ball);
            velocity.X.Should().BeApproximately(2, 5e-5f);
            velocity.Y.Should().BeApproximately(60 * world.DeltaTime, 5e-5f);
            world.AngularVelocityInOwnFrame(ball).Z.Should().BeApproximately(2, 5e-4f);

            // Forces act for one step only.
            world.Tick();
            world.VelocityInOwnFrame(ball).Y.Should().BeApproximately(60 * world.DeltaTime, 5e-5f);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void TorqueSpinsTheBody()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var pilot = new Pilot();
            Entity ball = Spawn(world, space, Ball(0.5f, mass: 2), At(0, 0, 0), pilot);
            Send(world, pilot, body => body.AddTorque(new Vector3(0, 12, 0)));
            world.Tick();
            // α = τ / I = 12 / 0.2 = 60 rad/s², for one step.
            world.AngularVelocityInOwnFrame(ball).Y.Should().BeApproximately(60 * world.DeltaTime, 5e-4f);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void KinematicTargetReachesTheTargetAndStops()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var pilot = new Pilot();
            Entity mover = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic), At(0, 0, 0), pilot);
            var target = new DPose(new Double3(0.5, 0, 0.25), DQuaternion.FromAxisAngle(Double3.UnitY, 0.3));
            Send(world, pilot, body => body.MoveKinematic(target));
            world.Tick();
            DPose pose = world.PoseInOwnFrame(mover);
            (pose.Position - target.Position).Length.Should().BeLessThan(1e-5);
            Math.Abs(Dot(pose.Rotation, target.Rotation)).Should().BeGreaterThan(1 - 1e-6);
            world.VelocityInOwnFrame(mover).X.Should().BeApproximately(0.5f / world.DeltaTime, 5e-3f);

            Tick(world, 10);
            (world.PoseInOwnFrame(mover).Position - target.Position).Length.Should().BeLessThan(1e-5);
            world.VelocityInOwnFrame(mover).Should().Be(Vector3.Zero);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void KinematicTargetDrivesDynamicBody()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var pilot = new Pilot();
            Entity box = Spawn(world, space, Box(0.5f), At(0, 0, 0), pilot);
            var target = new DPose(new Double3(0.2, 0.1, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.2));
            Send(world, pilot, body => body.MoveKinematic(target));
            world.Tick();
            (world.PoseInOwnFrame(box).Position - target.Position).Length.Should().BeLessThan(1e-4);
            Math.Abs(Dot(world.PoseInOwnFrame(box).Rotation, target.Rotation)).Should().BeGreaterThan(1 - 1e-5);
            Tick(world, 5);
            (world.PoseInOwnFrame(box).Position - target.Position).Length.Should().BeLessThan(1e-4);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void FrameHullTakesForcesFromItsOwnerIntent()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var pilot = new Pilot();
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new SphereShapeDesc(2), Mass: 500, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
            Components = [pilot],
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        Send(world, pilot, body => body.AddImpulse(new Vector3(0, 0, 1000)));

        world.Tick();

        FrameStepRecord sent = faults.Outputs[^1].Frames.Single(state => state.Frame == ship);
        world.Frames.LinearVelocity(ship).Should().Be(sent.Motion.Velocity, "the World takes the velocity of the frame from the output that Jolt sent");
        world.Frames.ProperAcceleration(ship).Should().Be(sent.ProperAcceleration!.Value, "a dynamic hull takes the proper acceleration that physics measured");
        world.Frames.LinearVelocity(ship).Z.Should().BeApproximately(2, 5e-5);
        // An impulse is a jolt: the accelerometer sees Δv / dt for that one step.
        world.Frames.ProperAcceleration(ship).Z.Should().BeApproximately(2 / world.DeltaTime, 0.05f);
        world.Tick();
        world.Frames.ProperAcceleration(ship).Length().Should().BeLessThan(1e-3f);
        world.Errors.Should().BeEmpty();
    }

    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
}
