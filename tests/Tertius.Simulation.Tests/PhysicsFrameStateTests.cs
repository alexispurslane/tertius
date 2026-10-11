using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A frame with the physics driver, such as a ship, is moved by physics: after each step physics reports a
/// <see cref="FrameState"/>, the frame's pose and motion relative to its parent. The world applies it to the frame, and
/// the frame's owner mirrors it. A physics double reports the states, so the tests decide exactly what physics said.
/// </summary>
public class PhysicsFrameStateTests
{
    /// <summary>A ship: a physics frame, whose owner has a hull body in the parent frame.</summary>
    private static Entity SpawnShip(TestWorld test, FrameHandle parent, out FrameHandle ship, DPose? pose = null)
    {
        Entity owner = test.World.Spawn(new SpawnDesc
        {
            Frame = parent,
            Pose = pose ?? DPose.Identity,
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Physics),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10)), Layer: BodyLayer.FrameHull),
        });
        ship = test.World.GetOwnedFrame(owner);
        return owner;
    }

    [Fact]
    public void AFrameStateSetsThePoseAndTheMotionOfAPhysicsFrame()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        SpawnShip(test, test.Root, out FrameHandle ship);
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(ship, TestWorld.At(5, 6, 7), new Double3(1, 2, 3), new Vector3(0, 0.5f, 0), new Vector3(0, -2, 0))));

        test.Run(1);

        test.World.Frames.LocalPose(ship).Position.Should().Be(new Double3(5, 6, 7));
        test.World.Frames.LinearVelocity(ship).Should().Be(new Double3(1, 2, 3));
        test.World.Frames.AngularVelocity(ship).Should().Be(new Vector3(0, 0.5f, 0));
        test.World.Frames.ProperAcceleration(ship).Should().Be(new Vector3(0, -2, 0));
    }

    [Fact]
    public void TheOwnerOfAPhysicsFrameMirrorsTheFrameStateAtOnce()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity owner = SpawnShip(test, test.Root, out FrameHandle ship);
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(ship, TestWorld.At(5, 6, 7), new Double3(1, 2, 3), new Vector3(0, 0.5f, 0), Vector3.Zero)));

        test.Run(1);

        test.World.GetPose(owner).Position.Should().Be(new Double3(5, 6, 7));
        test.World.GetVelocity(owner).Should().Be(new Vector3(1, 2, 3));
        test.World.GetAngularVelocity(owner).Should().Be(new Vector3(0, 0.5f, 0));
    }

    [Fact]
    public void TheEntitiesInAPhysicsFrameKeepTheirLocalPoseAndMoveWithTheFrame()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        SpawnShip(test, test.Root, out FrameHandle ship);
        Entity pilot = test.World.SpawnAt(ship, new Double3(0, 1, 0));
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(ship, TestWorld.At(5, 6, 7), Double3.Zero, Vector3.Zero, Vector3.Zero)));

        test.Run(1);

        test.World.GetPose(pilot).Position.Should().Be(new Double3(0, 1, 0));
        test.World.Snapshot.TryGetPoseIn(pilot, test.Root, out DPose inRoot).Should().BeTrue();
        PoseChecks.AssertNear(inRoot.Position, new Double3(5, 7, 7));
    }

    // Physics hears of a destroyed frame before its next step, and it drives a frame only through the hull of a frame
    // with the physics driver. So only a bug in the backend reports a state for a frame that is gone, a frame with
    // another driver, a frame whose owner has no body, or a root frame, whose owner has no hull.
#if TERTIUS_CONTRACTS
    [Fact]
    public void AFrameStateForAFrameThatIsGoneBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        SpawnShip(test, test.Root, out FrameHandle gone);
        test.World.Destroy(gone.Entity);
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(gone, TestWorld.At(5, 6, 7), Double3.Zero, Vector3.Zero, Vector3.Zero)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AFrameStateForAFrameThatPhysicsDoesNotDriveBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle dock = test.SpawnStaticFrame(TestWorld.At(100), radius: 50);
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(dock, TestWorld.At(5, 6, 7), Double3.Zero, Vector3.Zero, Vector3.Zero)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage("*physics does not drive it*");
    }

    [Fact]
    public void AFrameStateForAFrameWhoseOwnerHasNoBodyBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle drifter = SpawnBodilessPhysicsFrame(test);
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(drifter, TestWorld.At(5, 6, 7), Double3.Zero, Vector3.Zero, Vector3.Zero)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage("*has no body*");
    }

    [Fact]
    public void AFrameStateForARootFrameBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle raft = SpawnRootPhysicsFrameWithBody(test);
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(raft, TestWorld.At(1000), new Double3(5, 0, 0), Vector3.Zero, Vector3.Zero)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*state of {raft}*root frame*");
    }
#else
    [Fact]
    public void AFrameStateForAFrameThatIsGoneIsReportedOnceAndChangesNoFrame()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        SpawnShip(test, test.Root, out FrameHandle gone);
        SpawnShip(test, test.Root, out FrameHandle other, TestWorld.At(100));
        test.World.Destroy(gone.Entity);
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(gone, TestWorld.At(5, 6, 7), Double3.Zero, Vector3.Zero, Vector3.Zero)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"stale frame {gone}");
        test.World.Frames.LocalPose(other).Position.Should().Be(new Double3(100, 0, 0));
    }

    [Fact]
    public void AFrameStateForAFrameThatPhysicsDoesNotDriveIsReportedOnceAndMovesNothing()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle dock = test.SpawnStaticFrame(TestWorld.At(100), radius: 50);
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(dock, TestWorld.At(5, 6, 7), Double3.Zero, Vector3.Zero, Vector3.Zero)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"state of {dock}").And.Contain("physics does not drive it");
        test.World.Frames.LocalPose(dock).Should().Be(TestWorld.At(100));
        test.World.GetPose(dock.Entity).Should().Be(TestWorld.At(100));
    }

    [Fact]
    public void AFrameStateForAFrameWhoseOwnerHasNoBodyIsReportedOnceAndMovesNothing()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle drifter = SpawnBodilessPhysicsFrame(test);
        physics.OnStep((context, output) =>
            output.Frames.Add(new FrameState(drifter, TestWorld.At(5, 6, 7), Double3.Zero, Vector3.Zero, Vector3.Zero)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"state of {drifter}").And.Contain($"{drifter.Entity} has no body");
        test.World.Frames.LocalPose(drifter).Should().Be(TestWorld.At(100));
    }

    [Fact]
    public void AFrameStateForARootFrameIsReportedOnceAndMovesNothing()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle raft = SpawnRootPhysicsFrameWithBody(test);
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.Frames.Add(new FrameState(raft, TestWorld.At(1000), new Double3(5, 0, 0), Vector3.Zero, Vector3.Zero));
        });

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"state of {raft}").And.Contain("root frame");
        test.World.Frames.LocalPose(raft).Should().Be(DPose.Identity);
        test.World.Frames.LinearVelocity(raft).Should().Be(Double3.Zero);
    }
#endif

    /// <summary>
    /// A second root frame with the physics driver, whose owner has a body. A root frame has no parent, so the body is only
    /// the interior in the frame's own system, and physics has no hull to drive the frame with.
    /// </summary>
    private static FrameHandle SpawnRootPhysicsFrameWithBody(TestWorld test) => test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
    {
        AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Physics),
        Body = new BodyDesc(new SphereShapeDesc(10), Mass: 1),
    }));

    /// <summary>A frame with the physics driver, 100 m along +X, whose owner has no body, so physics has nothing to drive it with.</summary>
    private static FrameHandle SpawnBodilessPhysicsFrame(TestWorld test) => test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
    {
        Frame = test.Root,
        Pose = TestWorld.At(100),
        AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
    }));

    [Fact]
    public void PhysicsHearsOfAFrameNoLaterThanItHearsOfTheFramesHullBody()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<ChangeBatch> batches = physics.RecordChanges();
        using var test = new TestWorld(physics);
        Entity owner = SpawnShip(test, test.Root, out FrameHandle ship);

        test.Run(1);

        // The batch that creates the hull body in the root's physics system must not come before the batch that names the frame.
        int frameDeclared = batches.FindIndex(batch => batch.FramesCreated.Contains(ship));
        int hullCreated = batches.FindIndex(batch => batch.BodiesCreated.Any(create => create.Entity == owner && create.Frame == test.Root));
        frameDeclared.Should().BeGreaterThanOrEqualTo(0);
        hullCreated.Should().BeGreaterThanOrEqualTo(frameDeclared);
    }

    [Fact]
    public void AFrameStateIsRelativeToTheParentThatTheFramesHandOffGaveIt()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        Entity fighterOwner = SpawnShip(test, test.Root, out FrameHandle fighter, TestWorld.At(1100));
        physics.OnStep((context, output) =>
        {
            if (context.Tick != 0)
                return;
            output.HandOffs.Add(new HandOff(fighterOwner, test.Root, carrier, TestWorld.At(100), default, default));
            output.Frames.Add(new FrameState(fighter, TestWorld.At(102), new Double3(3, 0, 0), Vector3.Zero, Vector3.Zero));
        });

        test.Run(1);

        test.World.Frames.Parent(fighter).Should().Be(carrier);
        test.World.Frames.LocalPose(fighter).Position.Should().Be(new Double3(102, 0, 0));
        test.World.Frames.LinearVelocity(fighter).Should().Be(new Double3(3, 0, 0));
        test.World.GetFrame(fighterOwner).Should().Be(carrier);
        test.World.GetPose(fighterOwner).Position.Should().Be(new Double3(102, 0, 0));
    }

    [Fact]
    public void AFrameStateLandsBeforeTheFramesParentDiesAndIsConvertedToTheSurvivingAncestor()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        // The dock dies at the sync point of tick 0, the same one that applies the state of the shuttle.
        FrameHandle dock = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = new DPose(new Double3(1000, 0, 0), PoseChecks.QuarterTurnAboutY),
            AsFrame = new FrameSpec(FrameBounds.Sphere(500), MotionDriver.Static),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy())],
        }));
        FrameHandle shuttle = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = dock,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Layer: BodyLayer.FrameHull),
        }));
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.Frames.Add(new FrameState(shuttle, TestWorld.At(5), new Double3(2, 0, 0), Vector3.Zero, Vector3.Zero));
        });

        test.Run(1);

        // The state is relative to the dock. The quarter turn about +Y maps the dock's +X to the root's -Z, so the
        // shuttle, 5 m along the dock's +X, is at (1000, 0, -5) in the root, and its 2 m/s are along -Z.
        test.World.Frames.IsAlive(dock).Should().BeFalse();
        test.World.Frames.Parent(shuttle).Should().Be(test.Root);
        PoseChecks.AssertNear(test.World.Frames.LocalPose(shuttle).Position, new Double3(1000, 0, -5));
        PoseChecks.AssertNear(test.World.Frames.LinearVelocity(shuttle), new Double3(0, 0, -2));
        test.World.GetFrame(shuttle.Entity).Should().Be(test.Root);
        test.World.GetPose(shuttle.Entity).Should().Be(test.World.Frames.LocalPose(shuttle));
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AHullThatTheDestroyBatchMovesStartsFromItsFramesNewState()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<ChangeBatch> batches = physics.RecordChanges();
        using var test = new TestWorld(physics);
        FrameHandle dock = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = new DPose(new Double3(1000, 0, 0), PoseChecks.QuarterTurnAboutY),
            AsFrame = new FrameSpec(FrameBounds.Sphere(500), MotionDriver.Static),
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy())],
        }));
        Entity shuttleOwner = SpawnShip(test, dock, out FrameHandle shuttle);
        // Physics reports only the frame's state, not the hull's own body state.
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.Frames.Add(new FrameState(shuttle, TestWorld.At(5), new Double3(2, 0, 0), Vector3.Zero, Vector3.Zero));
        });

        test.Run(1);

        // The dock died at this sync point, so the hull moved to the root's physics system. It starts from the pose and
        // velocity after the step, converted through the dock's quarter turn, not from the pose before the step.
        BodyCreate hull = batches.SelectMany(batch => batch.BodiesCreated).Last(create => create.Entity == shuttleOwner);
        hull.Frame.Should().Be(test.Root);
        PoseChecks.AssertNear(hull.Pose.Position, new Double3(1000, 0, -5));
        PoseChecks.AssertNear(hull.LinearVelocity, new Vector3(0, 0, -2));
        test.World.Errors.Should().BeEmpty();
    }
}
