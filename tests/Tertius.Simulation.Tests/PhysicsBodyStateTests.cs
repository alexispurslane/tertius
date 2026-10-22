using System.Numerics;
using AwesomeAssertions;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Physics owns the transform of every entity with a body. After each step it reports a <see cref="BodyState"/> for each
/// body that moved, and the world copies the state into the entity's transform at the sync point. These tests run the
/// world on real Jolt. The wrapper <see cref="FaultInjectingBackend"/> records the output that Jolt really sent, and the
/// tests compare the world with it. A test of an output that Jolt never produces (two states for one body, a state
/// for a body that is gone) edits the output of one step.
/// </summary>
public class PhysicsBodyStateTests
{
    /// <summary>How far the edits below push the pose of a state that they add, in meters along +X.</summary>
    private const double Shift = 100;

    /// <summary>The world on the wrapper, with a ground frame whose gravity of 9.81 m/s² along -Y makes every ball fall, so that Jolt reports it in every step.</summary>
    private static World NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground)
    {
        World world = NewWorldWithFaults(out faults);
        ground = Ground(world, 9.81f);
        return world;
    }

    private static BodyState StateOf(PhysicsStepOutput output, Entity entity) =>
        output.Bodies.Single(state => state.Entity == entity);

    /// <summary>The same state with its pose moved by <see cref="Shift"/>, so that a world that applied it would show.</summary>
    private static BodyState Shifted(BodyState state) =>
        state with { Pose = state.Pose with { Position = state.Pose.Position + new Double3(Shift, 0, 0) } };

    [Fact]
    public void ABodyStateBecomesThePoseAndVelocitiesOfTheEntityAtTheEndOfTheTick()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        DPose spawnPose = At(0, 10);
        Entity ball = world.Spawn(new()
        {
            Frame = ground,
            Pose = spawnPose,
            LinearVelocity = new(1, 0, -1),
            AngularVelocity = new(0, 2, 0),
            Body = Ball()
        });

        world.Run(1);

        BodyState sent = StateOf(faults.Outputs.Single(), ball);
        sent.Pose.Should().NotBe(spawnPose, "Jolt moved the falling and spinning ball in its step");
        sent.AngularVelocity.Should().NotBe(Vector3.Zero);
        world.PoseInOwnFrame(ball).Should().Be(sent.Pose);
        world.VelocityInOwnFrame(ball).Should().Be(sent.LinearVelocity);
        world.AngularVelocityInOwnFrame(ball).Should().Be(sent.AngularVelocity);
    }

    // Jolt reports a body in the frame that holds its physics system. The World takes any live frame in a state as the
    // frame of the entity, so a state in another frame moves the entity, with no hand-off. Jolt never sends that on its
    // own, so the test edits the output: it puts the real state of the ball into the station frame in every step.
    [Fact]
    public void ABodyStateThatNamesAnotherFrameMovesTheEntityToIt()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        FrameHandle station = world.SpawnStaticFrame(ground, At(500), radius: 100);
        var log = new Recorder(EventMask.Of<FrameChanged>());
        Entity ball = world.Spawn(new() { Frame = ground, Pose = At(0, 10), Body = Ball(), Components = [log] });
        faults.EditOutput = output =>
        {
            int index = output.Bodies.FindIndex(state => state.Entity == ball);
            output.Bodies[index] = output.Bodies[index] with { Frame = station };
        };

        world.Run(3);

        world.GetFrame(ball).Should().Be(station);
        log.Seen.Should().ContainSingle("the move is told once, though the output names the station in every step");
        BodyState sent = StateOf(faults.Outputs[^1], ball);
        sent.Frame.Should().Be(station);
        world.PoseInOwnFrame(ball).Should().Be(sent.Pose);
    }

    [Fact]
    public void WhenPhysicsReportsOneBodyTwiceInAStepTheLaterStateWins()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        Entity ball = world.Spawn(new() { Frame = ground, Pose = At(0, 10), Body = Ball() });
        faults.EditNextOutput = output => output.Bodies.Add(Shifted(StateOf(output, ball)));

        world.Run(1);

        BodyState[] sent = [.. faults.Outputs.Single().Bodies.Where(state => state.Entity == ball)];
        sent.Should().HaveCount(2);
        sent[1].Pose.Should().NotBe(sent[0].Pose,
            "the two states must differ, or the test cannot tell which one the world took");
        world.PoseInOwnFrame(ball).Should().Be(sent[1].Pose);
    }

    // Physics hears of every destroyed entity and frame before its next step, and an entity keeps its body for life. So
    // a body state names a live entity with a body and a live frame, unless the backend has a bug. Jolt never sends one
    // that does not, so each test below adds the broken state to the output of the first step.
#if TERTIUS_CONTRACTS
    [Fact]
    public void ABodyStateForAnEntityWithoutABodyBreaksAContract()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        Entity ball = world.Spawn(new() { Frame = ground, Pose = At(0, 10), Body = Ball() });
        Entity bodiless = world.Spawn(new() { Frame = ground, Pose = At(3) });
        faults.EditNextOutput = output => output.Bodies.Add(StateOf(output, ball) with { Entity = bodiless });

        Action tick = world.Tick;

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void ABodyStateForAnEntityThatNoLongerExistsBreaksAContract()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        Entity gone = world.Spawn(new() { Frame = ground, Pose = At(0, 10), Body = Ball() });
        Entity bystander = world.Spawn(new() { Frame = ground, Pose = At(3, 10), Body = Ball() });
        world.Destroy(gone);
        faults.EditNextOutput = output => output.Bodies.Add(StateOf(output, bystander) with { Entity = gone });

        Action tick = world.Tick;

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void ABodyStateThatNamesNoFrameBreaksAContract()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        Entity ball = world.Spawn(new() { Frame = ground, Pose = At(0, 10), Body = Ball() });
        faults.EditNextOutput = output =>
            output.Bodies[output.Bodies.FindIndex(state => state.Entity == ball)] =
                StateOf(output, ball) with { Frame = FrameHandle.Null };

        Action tick = world.Tick;

        tick.Should().Throw<ContractException>();
    }
#else
    [Fact]
    public void ABodyStateForAnEntityWithoutABodyIsReportedOnceAndChangesNothing()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 10), Body = Ball() });
        Entity bodiless = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(3) });
        faults.EditNextOutput = output => output.Bodies.Add(Shifted(StateOf(output, ball)) with { Entity = bodiless });

        world.Run(1);

        world.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {bodiless}").And.Contain("has no body");
        world.PoseInOwnFrame(bodiless).Position.Should().Be(new Double3(3, 0, 0));
        world.PoseInOwnFrame(ball).Should().Be(StateOf(faults.Outputs.Single(), ball).Pose, "the state of the ball is not part of the broken output");
    }

    [Fact]
    public void ABodyStateForAnEntityThatNoLongerExistsIsReportedOnceAndChangesNothing()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        Entity gone = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 10), Body = Ball() });
        Entity bystander = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(3, 10), Body = Ball() });
        world.Destroy(gone);
        faults.EditNextOutput = output => output.Bodies.Add(Shifted(StateOf(output, bystander)) with { Entity = gone });

        world.Run(1);

        world.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {gone}").And.Contain("no longer exists");
        world.PoseInOwnFrame(bystander).Should().Be(StateOf(faults.Outputs.Single(), bystander).Pose, "the bystander takes the state that Jolt sent for it");
    }

    [Fact]
    public void ABodyStateWithAStaleHandleDoesNotMoveTheEntityThatReusedTheSlot()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        Entity gone = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 10), Body = Ball() });
        world.Destroy(gone);
        Entity successor = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(3, 10), Body = Ball() });
        successor.Index.Should().Be(gone.Index, "the test needs the successor to sit in the slot of the entity that was destroyed");
        faults.EditNextOutput = output => output.Bodies.Add(Shifted(StateOf(output, successor)) with { Entity = gone });

        world.Run(1);

        world.PoseInOwnFrame(successor).Should().Be(StateOf(faults.Outputs.Single(), successor).Pose, "the stale state came last, and the world did not apply it to the successor");
        world.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {gone}");
    }

    [Fact]
    public void EachBrokenOutputAboutOneBodyIsReportedOnce()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        FrameHandle goneFrame = world.SpawnStaticFrame(ground, At(500), radius: 10);
        world.Destroy(goneFrame.Entity);
        DPose spawnPose = At(3, 10);
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = spawnPose, Body = Ball() });
        BodyState real = default;
        faults.EditNextOutput = output =>
        {
            real = StateOf(output, ball);
            output.Bodies.Add(Shifted(real) with { Frame = goneFrame });
            output.Bodies.Add(Shifted(real) with { Frame = FrameHandle.Null });
        };

        world.Run(1);

        world.Errors.Should().HaveCount(2).And.OnlyContain(error => error.Contains($"body state of {ball}"));
        real.Pose.Should().NotBe(spawnPose, "Jolt moved the ball, so only a skipped update can leave it at the spawn pose");
        world.PoseInOwnFrame(ball).Should().Be(spawnPose, "a broken output skips the whole update of the body, also the real state of it");
    }

    [Fact]
    public void ABodyStateThatNamesNoFrameIsReportedOnceAndChangesNothing()
    {
        using World world = NewFallingWorld(out FaultInjectingBackend faults, out FrameHandle ground);
        FrameHandle station = world.SpawnStaticFrame(ground, At(500), radius: 100);
        DPose spawnPose = At(3, 10);
        Entity ball = world.Spawn(new SpawnDesc { Frame = station, Pose = spawnPose, LinearVelocity =
 new Double3(0, 2, 0), Body = Ball() });
        BodyState real = default;
        faults.EditNextOutput = output =>
        {
            real = StateOf(output, ball);
            output.Bodies.Add(real with { Frame = FrameHandle.Null });
        };

        world.Run(1);

        world.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {ball}").And.Contain("names no frame");
        world.GetFrame(ball).Should().Be(station);
        real.Pose.Should().NotBe(spawnPose, "Jolt moved the ball, so only a skipped update can leave it at the spawn pose");
        world.PoseInOwnFrame(ball).Should().Be(spawnPose);
    }
#endif

    [Fact]
    public void AnIntegratingBackendMovesABodyByItsVelocityAndTheResultReachesTheSnapshot()
    {
        // At 64 ticks per second a step is exactly 1/64 s, so three steps at 8 m/s move the ball 24/64 = 0.375 m. The ground has no gravity.
        using World world =
            NewWorldWithFaults(out FaultInjectingBackend faults, configure: options => options.TickRate = 64);
        FrameHandle space = Ground(world, 0);
        Entity ball = world.Spawn(new() { Frame = space, Pose = At(1), LinearVelocity = new(8, 0, 0), Body = Ball() });

        world.Run(3);

        BodyState sent = StateOf(faults.Outputs[^1], ball);
        faults.Outputs.Should().HaveCount(3);
        world.PoseInOwnFrame(ball).Should().Be(sent.Pose, "the world holds the pose that Jolt sent in the last step");
        world.VelocityInOwnFrame(ball).Should().Be(sent.LinearVelocity);
        sent.Pose.Position.X.Should().Be(1.375, "Jolt moved the ball by its velocity in three steps of 1/64 s");
        sent.LinearVelocity.X.Should().Be(8f);
    }

    [Fact]
    public void WithoutAPhysicsBackendABodyStaysWhereItWasSpawnedAndNoErrorIsReported()
    {
        using var test = new TestWorld();
        Entity ball = test.World.Spawn(new()
        {
            Frame = test.Root,
            Pose = TestWorld.At(2),
            LinearVelocity = new Vector3(5, 0, 0),
            Body = TestWorld.Ball
        });

        test.Run(5);

        test.World.PoseInOwnFrame(ball).Position.X.Should().Be(2);
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void WithoutAPhysicsBackendTheIntentsOfOneTickDoNotPileUpIntoTheNext()
    {
        using var test = new TestWorld();
        var pendingAtTheStartOfEachTick = new List<float>();
        test.World.Spawn(new()
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components =
            [
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                {
                    pendingAtTheStartOfEachTick.Add(self.Body.Pending.Force.Y);
                    self.Body.AddForce(Vector3.UnitY);
                })
            ]
        });

        test.Run(3);

        test.World.Errors.Should().BeEmpty();
        pendingAtTheStartOfEachTick.Should().Equal(0, 0, 0);
    }
}
