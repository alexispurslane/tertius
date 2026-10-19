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

    private static void Send(World world, SelfPilot pilot, SelfCommand command)
    {
        pilot.Once = command;
        world.Tick();
    }

    private static Entity Spawn(World world, FrameHandle frame, BodyDesc body, DPose pose, IComponent pilot, Double3 velocity = default, Vector3 angularVelocity = default) =>
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
    public void ATeleportAndAnEndPoseOrASteerTargetInOneIntentMoveTheBodyOnFromTheTeleportPose()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var moverPilot = new SelfPilot();
            var boxPilot = new SelfPilot();
            Entity mover = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic), At(0, 0, 0), moverPilot);
            Entity box = Spawn(world, space, Box(0.5f), At(0, 10, 0), boxPilot);
            world.Tick();

            // Each body teleports 20 m away and, in the same intent, gets an aim pose half a meter and 0.1 rad further on:
            // an end pose for the kinematic body, and a steer target for the dynamic box.
            var offsets = new Dictionary<Entity, Double3> { [mover] = new(20, 0, 0), [box] = new(0, 20, 0) };
            foreach (var (entity, pilot) in new[] { (mover, moverPilot), (box, boxPilot) })
            {
                Double3 offset = offsets[entity];
                bool steer = entity == box;
                pilot.Once = self =>
                {
                    self.Body.Teleport(new DPose(offset, DQuaternion.FromAxisAngle(Double3.UnitY, 0.4)));
                    var aim = new DPose(offset + new Double3(0.5, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5));
                    if (steer)
                        self.Body.SteerTo(aim);
                    else
                        self.MoveKinematic(aim);
                };
            }
            world.Tick();
            world.Tick();

            // Teleport first, then the end pose or the steer target: kinematic and dynamic bodies end the step at the aim
            // pose, moving at the speed that carried them there from the teleport pose.
            foreach (Entity entity in new[] { mover, box })
            {
                DPose pose = world.PoseInOwnFrame(entity);
                DPose target = new(offsets[entity] + new Double3(0.5, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5));
                (pose.Position - target.Position).Length.Should().BeLessThan(1e-4, "{0} ends at its target", entity);
                Math.Abs(Dot(pose.Rotation, target.Rotation)).Should().BeGreaterThan(1 - 1e-5);
                world.VelocityInOwnFrame(entity).X.Should().BeApproximately(0.5f / world.DeltaTime, 1e-3f);
                world.AngularVelocityInOwnFrame(entity).Y.Should().BeApproximately(0.1f / world.DeltaTime, 1e-3f);
            }
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ATorqueTurnsADynamicBodyForOneStep()
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
    public void MoveKinematicWritesAnEndPoseThatTheNextStepApplies()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var pilot = new SelfPilot();
        Entity mover = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic), At(0, 0, 0), pilot);
        var end = new DPose(new Double3(0.5, 0, 0.25), DQuaternion.FromAxisAngle(Double3.UnitY, 0.3));

        Send(world, pilot, self => self.MoveKinematic(end));
        world.Tick();

        PhysicsIntent applied = faults.Intents.Should().ContainSingle(record => record.Entity == mover, "only the step after the write applies it").Subject.Intent;
        applied.Move.TryGetValue(out EndPose written).Should().BeTrue("MoveKinematic writes the case EndPose of the motion intent, not {0}", applied.Move.Value);
        written.Pose.Position.Should().Be(end.Position);
        Math.Abs(Dot(written.Pose.Rotation, end.Rotation)).Should().BeGreaterThan(1 - 1e-12);
        DPose pose = world.PoseInOwnFrame(mover);
        (pose.Position - end.Position).Length.Should().BeLessThan(1e-5, "the step that applies the end pose ends with the body on it");
        Math.Abs(Dot(pose.Rotation, end.Rotation)).Should().BeGreaterThan(1 - 1e-6);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnEndPoseMovesAKinematicBodyThereAndStopsIt()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var pilot = new SelfPilot();
            Entity mover = Spawn(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic), At(0, 0, 0), pilot);
            var target = new DPose(new Double3(0.5, 0, 0.25), DQuaternion.FromAxisAngle(Double3.UnitY, 0.3));
            Send(world, pilot, self => self.MoveKinematic(target));
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
    public void ASteerTargetCarriesADynamicBodyToItsPoseOverTheStep()
    {
        var (world, space) = ZeroGravity();
        using (world)
        {
            var pilot = new Pilot();
            Entity box = Spawn(world, space, Box(0.5f), At(0, 0, 0), pilot);
            var target = new DPose(new Double3(0.2, 0.1, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.2));
            Send(world, pilot, body => body.SteerTo(target));
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
