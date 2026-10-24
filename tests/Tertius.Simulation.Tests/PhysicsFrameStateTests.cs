using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A frame with a dynamic hull, such as a ship, is moved by physics: after each step physics reports a
/// <c>FrameState</c>, the frame's pose and motion relative to its parent. The world applies it to the frame, and
/// the frame's owner mirrors it. The tests run the world on real Jolt and compare the world with the states that Jolt really
/// sent. A test of a state that only a bug produces adds it to the output of one step, through
/// <see cref="FaultInjectingBackend"/>.
/// </summary>
public sealed class PhysicsFrameStateTests
{
    /// <summary>
    /// A ship: a physics frame, whose owner has a hull body in the parent frame. Nothing slows the hull down, so it moves
    /// with the velocity and the spin that it starts with.
    /// </summary>
    private static Entity SpawnShip(
        TestWorld test,
        FrameHandle parent,
        out FrameHandle ship,
        DPose? pose = null,
        Vector3 velocity = default,
        Vector3 spin = default,
        params IComponent[] components
    )
    {
        Entity owner = test.World.Spawn(
            new()
            {
                Frame = parent,
                Pose = pose ?? DPose.Identity,
                LinearVelocity = velocity,
                AngularVelocity = spin,
                AsFrame = new(FrameBounds.Sphere(20)),
                Body = new(
                    Shape: new BoxShapeDesc(new(10)),
                    Layer: BodyLayer.FrameHull,
                    LinearDamping: 0,
                    AngularDamping: 0
                ),
                Components = components,
            }
        );
        ship = test.World.GetOwnedFrame(owner);
        return owner;
    }

    /// <summary>The state of the frame that Jolt sent in the step of tick <paramref name="tick"/>.</summary>
    private static FrameStepRecord StateSent(FaultInjectingBackend faults, FrameHandle frame, int tick = 0) =>
        faults.Outputs[tick].Frames.Single(state => state.Frame == frame);

    [Fact]
    public void AFrameStateSetsThePoseAndTheMotionOfAPhysicsFrame()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        // The ship moves and spins. A force that acts in the step of tick 1 gives it a proper acceleration.
        var pilot = new Pilot { Once = body => body.AddForce(new(0, -2, 0)) };
        SpawnShip(
            test: test,
            parent: test.Root,
            ship: out FrameHandle ship,
            pose: TestWorld.At(1, 2, 3),
            velocity: new(1, 2, 3),
            spin: new(0, 0.5f, 0),
            pilot
        );

        test.Run(2);

        FrameStepRecord sent = StateSent(faults, ship, tick: 1);
        sent.Motion.Pose.Position.Should().NotBe(new Double3(1, 2, 3), "the ship moved");
        sent.Motion.AngularVelocity.Should().NotBe(Double3.Zero);
        sent.ProperAcceleration.Should().NotBe(Vector3.Zero, "the force acted in the step");
        test.World.Frames.LocalPose(ship).Should().Be(sent.Motion.Pose);
        test.World.Frames.LinearVelocity(ship).Should().Be(sent.Motion.Velocity);
        test.World.Frames.AngularVelocity(ship).Should().Be(sent.Motion.AngularVelocity.ToVector3());
        test.World.Frames.ProperAcceleration(ship).Should().Be(sent.ProperAcceleration!.Value);
    }

    [Fact]
    public void TheOwnerOfAPhysicsFrameMirrorsTheFrameStateAtOnce()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        Entity owner = SpawnShip(
            test: test,
            parent: test.Root,
            ship: out FrameHandle ship,
            pose: TestWorld.At(1, 2, 3),
            velocity: new(1, 2, 3),
            spin: new(0, 0.5f, 0)
        );

        test.Run(1);

        FrameStepRecord sent = StateSent(faults, ship);
        sent.Motion.Pose.Position.Should().NotBe(new Double3(1, 2, 3), "the ship moved");
        test.World.PoseInOwnFrame(owner).Should().Be(sent.Motion.Pose);
        test.World.VelocityInOwnFrame(owner).Should().Be(sent.Motion.Velocity.ToVector3());
        test.World.AngularVelocityInOwnFrame(owner).Should().Be(sent.Motion.AngularVelocity.ToVector3());
    }

    [Fact]
    public void TheEntitiesInAPhysicsFrameKeepTheirLocalPoseAndMoveWithTheFrame()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        SpawnShip(test: test, parent: test.Root, ship: out FrameHandle ship, velocity: new(6, 0, 0));
        Entity pilot = test.World.SpawnAt(ship, new(0, 1, 0));

        test.Run(1);

        // The ship flew 0.1 m along +X in its step. The pilot is 1 m above the ship's origin and did not move in the ship.
        FrameStepRecord sent = StateSent(faults, ship);
        PoseChecks.AssertNear(sent.Motion.Pose.Position, new(0.1, 0, 0), 1e-6);
        test.World.PoseInOwnFrame(pilot).Position.Should().Be(new Double3(0, 1, 0));
        DPose inRoot = test.World.Snapshot.PoseIn(pilot, test.Root).Found();
        PoseChecks.AssertNear(inRoot.Position, sent.Motion.Pose.Position + new Double3(0, 1, 0));
    }

    // Physics hears of a destroyed frame before its next step, and it drives a frame only through the hull of a frame
    // with a dynamic hull. So only a bug in the backend reports a state for a frame that is gone, a frame with a hull of
    // another motion, a frame whose owner has no body, or a root frame, whose owner has no hull.
#if TERTIUS_CONTRACTS
    [Fact]
    public void AFrameStateForAFrameThatIsGoneBreaksAContract()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        SpawnShip(test: test, parent: test.Root, ship: out FrameHandle gone);
        test.World.Destroy(gone.Entity);
        faults.EditNextOutput = output =>
            output.Frames.Add(
                StepRecords.Of(
                    frame: gone,
                    pose: TestWorld.At(5, 6, 7),
                    velocity: Double3.Zero,
                    angularVelocity: Vector3.Zero,
                    measured: Vector3.Zero
                )
            );

        Action tick = test.World.Tick;

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AFrameStateForAFrameWhoseOwnerHasNoBodyBreaksAContract()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        FrameHandle drifter = SpawnBodilessPhysicsFrame(test);
        faults.EditNextOutput = output =>
            output.Frames.Add(
                StepRecords.Of(
                    frame: drifter,
                    pose: TestWorld.At(5, 6, 7),
                    velocity: Double3.Zero,
                    angularVelocity: Vector3.Zero,
                    measured: Vector3.Zero
                )
            );

        Action tick = test.World.Tick;

        tick.Should().Throw<ContractException>().WithMessage("*has no body*");
    }

    [Fact]
    public void AFrameStateForARootFrameBreaksAContract()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        FrameHandle raft = SpawnRootPhysicsFrameWithBody(test);
        faults.EditNextOutput = output =>
            output.Frames.Add(
                StepRecords.Of(
                    frame: raft,
                    pose: TestWorld.At(1000),
                    velocity: new(5, 0, 0),
                    angularVelocity: Vector3.Zero,
                    measured: Vector3.Zero
                )
            );

        Action tick = test.World.Tick;

        tick.Should().Throw<ContractException>().WithMessage($"*step record of {raft}*root frame*");
    }
#else
    [Fact]
    public void AFrameStateForAFrameThatIsGoneIsReportedOnceAndChangesNoFrame()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        SpawnShip(test: test, parent: test.Root, ship: out FrameHandle gone);
        SpawnShip(test: test, parent: test.Root, ship: out FrameHandle other, pose: TestWorld.At(100));
        test.World.Destroy(gone.Entity);
        faults.EditNextOutput = output =>
            output.Frames.Add(
                StepRecords.Of(
                    frame: gone,
                    pose: TestWorld.At(5, 6, 7),
                    velocity: Double3.Zero,
                    angularVelocity: Vector3.Zero,
                    measured: Vector3.Zero
                )
            );

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"stale frame {gone}");
        test.World.Frames.LocalPose(other).Position.Should().Be(new Double3(100, 0, 0));
    }

    [Fact]
    public void AFrameStateForAFrameWhoseOwnerHasNoBodyIsReportedOnceAndMovesNothing()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        FrameHandle drifter = SpawnBodilessPhysicsFrame(test);
        faults.EditNextOutput = output =>
            output.Frames.Add(
                StepRecords.Of(
                    frame: drifter,
                    pose: TestWorld.At(5, 6, 7),
                    velocity: Double3.Zero,
                    angularVelocity: Vector3.Zero,
                    measured: Vector3.Zero
                )
            );

        test.Run(1);

        test.World.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain($"step record of {drifter}")
            .And.Contain($"{drifter.Entity} has no body");
        test.World.Frames.LocalPose(drifter).Should().Be(TestWorld.At(100));
    }

    [Fact]
    public void AFrameStateForARootFrameIsReportedOnceAndMovesNothing()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        FrameHandle raft = SpawnRootPhysicsFrameWithBody(test);
        faults.EditNextOutput = output =>
            output.Frames.Add(
                StepRecords.Of(
                    frame: raft,
                    pose: TestWorld.At(1000),
                    velocity: new Double3(5, 0, 0),
                    angularVelocity: Vector3.Zero,
                    measured: Vector3.Zero
                )
            );

        test.Run(1);

        test.World.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain($"step record of {raft}")
            .And.Contain("root frame");
        test.World.Frames.LocalPose(raft).Should().Be(DPose.Identity);
        test.World.Frames.LinearVelocity(raft).Should().Be(Double3.Zero);
    }
#endif

    /// <summary>
    /// A second root frame whose owner has a dynamic body. A root frame has no parent, so the body is only
    /// the interior in the frame's own system, and physics has no hull to drive the frame with.
    /// </summary>
    private static FrameHandle SpawnRootPhysicsFrameWithBody(TestWorld test) =>
        test.World.GetOwnedFrame(
            test.World.Spawn(
                new() { AsFrame = new(FrameBounds.Sphere(1e6f)), Body = new(Shape: new SphereShapeDesc(10), Mass: 1) }
            )
        );

    /// <summary>A frame 100 m along +X whose owner has no body, so physics has nothing to drive it with.</summary>
    private static FrameHandle SpawnBodilessPhysicsFrame(TestWorld test) =>
        test.World.GetOwnedFrame(
            test.World.Spawn(
                new()
                {
                    Frame = test.Root,
                    Pose = TestWorld.At(100),
                    AsFrame = new(FrameBounds.Sphere(10)),
                }
            )
        );

    [Fact]
    public void PhysicsHearsOfAFrameNoLaterThanItHearsOfTheFramesHullBody()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        List<PhysicsChanges> batches = faults.AppliedChanges;
        Entity owner = SpawnShip(test: test, parent: test.Root, ship: out FrameHandle ship);

        test.Run(1);

        // The batch that creates the hull body in the root's physics system must not come before the batch that names the frame.
        int frameDeclared = batches.FindIndex(batch => batch.FramesCreated.Contains(ship));
        int hullCreated = batches.FindIndex(batch =>
            batch.BodiesCreated.Any(create => create.Entity == owner && create.Frame == test.Root)
        );
        frameDeclared.Should().BeGreaterThanOrEqualTo(0);
        hullCreated.Should().BeGreaterThanOrEqualTo(frameDeclared);
    }

    // The fighter starts inside the bubble of the carrier, so Jolt hands it off to the carrier in the first step, and sends
    // the state of its frame relative to the carrier.
    [Fact]
    public void AFrameStateIsRelativeToTheParentThatTheFramesHandOffGaveIt()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        Entity fighterOwner = SpawnShip(
            test: test,
            parent: test.Root,
            ship: out FrameHandle fighter,
            pose: TestWorld.At(1100),
            velocity: new(6, 0, 0)
        );

        test.Run(1);

        faults.Outputs[0].HandOffs.Should().ContainSingle().Which.To.Should().Be(carrier);
        FrameStepRecord sent = StateSent(faults, fighter);
        PoseChecks.AssertNear(sent.Motion.Pose.Position, new(100.1, 0, 0), 1e-6);
        test.World.Frames.Parent(fighter).Should().Be(carrier);
        test.World.Frames.LocalPose(fighter)
            .Should()
            .Be(sent.Motion.Pose, "the state is relative to the carrier, so the world keeps it as it is");
        test.World.Frames.LinearVelocity(fighter).Should().Be(sent.Motion.Velocity);
        PoseChecks.AssertNear(sent.Motion.Velocity, new(6, 0, 0), 1e-6);
        test.World.GetFrame(fighterOwner).Should().Be(carrier);
        test.World.PoseInOwnFrame(fighterOwner).Position.Should().Be(sent.Motion.Pose.Position);
    }

    /// <summary>
    /// A dock that destroys itself in tick 0, in the root, 1000 m along +X and turned a quarter turn about +Y. The turn maps the
    /// dock's +X to the root's -Z.
    /// </summary>
    private static FrameHandle SpawnDockThatDiesInTick0(TestWorld test) =>
        test.World.GetOwnedFrame(
            test.World.Spawn(
                new()
                {
                    Frame = test.Root,
                    Pose = new(new(1000, 0, 0), PoseChecks.QuarterTurnAboutY),
                    AsFrame = new(FrameBounds.Sphere(500)),
                    Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy())],
                }
            )
        );

    /// <summary>Where a point of the dock is in the root, if the dock is the one of <see cref="SpawnDockThatDiesInTick0"/>.</summary>
    private static Double3 DockPointInRoot(Double3 pointInDock) =>
        new Double3(1000, 0, 0) + PoseChecks.QuarterTurnAboutY.Rotate(pointInDock);

    [Fact]
    public void AFrameStateLandsBeforeTheFramesParentDiesAndIsConvertedToTheSurvivingAncestor()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        // The dock dies at the sync point of tick 0, the same one that applies the state of the shuttle.
        FrameHandle dock = SpawnDockThatDiesInTick0(test);
        SpawnShip(
            test: test,
            parent: dock,
            ship: out FrameHandle shuttle,
            pose: TestWorld.At(5),
            velocity: new(2, 0, 0)
        );

        test.Run(1);

        // The state is relative to the dock: about 5 m along the dock's +X, and 2 m/s along it. The quarter turn about +Y
        // maps the dock's +X to the root's -Z, so the shuttle is at about (1000, 0, -5) in the root, and moves along -Z.
        FrameStepRecord sent = StateSent(faults, shuttle);
        sent.Motion.Pose.Position.X.Should().BeGreaterThan(5, "the shuttle moved along the dock's +X in its step");
        test.World.Frames.IsAlive(dock).Should().BeFalse();
        test.World.Frames.Parent(shuttle).Should().Be(test.Root);
        PoseChecks.AssertNear(
            test.World.Frames.LocalPose(shuttle).Position,
            DockPointInRoot(sent.Motion.Pose.Position)
        );
        PoseChecks.AssertNear(
            test.World.Frames.LinearVelocity(shuttle),
            PoseChecks.QuarterTurnAboutY.Rotate(sent.Motion.Velocity)
        );
        PoseChecks.AssertNear(test.World.Frames.LinearVelocity(shuttle), new(0, 0, -2), 1e-6);
        test.World.GetFrame(shuttle.Entity).Should().Be(test.Root);
        test.World.PoseInOwnFrame(shuttle.Entity).Should().Be(test.World.Frames.LocalPose(shuttle));
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AHullThatTheDestroyBatchMovesStartsFromItsFramesNewState()
    {
        using var test = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        List<PhysicsChanges> batches = faults.AppliedChanges;
        FrameHandle dock = SpawnDockThatDiesInTick0(test);
        Entity shuttleOwner = SpawnShip(
            test: test,
            parent: dock,
            ship: out FrameHandle shuttle,
            pose: TestWorld.At(5),
            velocity: new(2, 0, 0)
        );

        test.Run(1);

        // The dock died at this sync point, so the hull moved to the root's physics system. It starts from the pose and
        // velocity after the step, converted through the dock's quarter turn, not from the pose before the step.
        FrameStepRecord sent = StateSent(faults, shuttle);
        BodyCreate hull = batches
            .SelectMany(batch => batch.BodiesCreated)
            .Last(create => create.Entity == shuttleOwner);
        hull.Frame.Should().Be(test.Root);
        PoseChecks.AssertNear(hull.Pose.Position, DockPointInRoot(sent.Motion.Pose.Position));
        PoseChecks.AssertNear(hull.LinearVelocity, PoseChecks.QuarterTurnAboutY.Rotate(sent.Motion.Velocity));
        PoseChecks.AssertNear(hull.Pose.Position, new(1000, 0, -5), 0.1);
        test.World.Errors.Should().BeEmpty();
    }
}
