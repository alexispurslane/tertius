using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Physics owns the transform of every entity with a body. After each step it reports a <see cref="BodyState"/> for each
/// body that moved, and the world copies the state into the entity's transform at the sync point. A physics double
/// reports the states, so the tests decide exactly what physics said.
/// </summary>
public class PhysicsBodyStateTests
{
    private static readonly DPose ReportedPose = TestWorld.At(7, 8, 9);
    private static readonly Vector3 ReportedVelocity = new(1, 0, -1);
    private static readonly Vector3 ReportedSpin = new(0, 2, 0);

    [Fact]
    public void ABodyStateBecomesThePoseAndVelocitiesOfTheEntityAtTheEndOfTheTick()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        physics.OnStep((context, output) => output.Bodies.Add(new BodyState(ball, test.Root, ReportedPose, ReportedVelocity, ReportedSpin)));

        test.Run(1);

        test.World.GetPose(ball).Should().Be(ReportedPose);
        test.World.GetVelocity(ball).Should().Be(ReportedVelocity);
        test.World.GetAngularVelocity(ball).Should().Be(ReportedSpin);
    }

    [Fact]
    public void ABodyStateThatNamesAnotherFrameMovesTheEntityToIt()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        var log = new Recorder(EventMask.Of<FrameChanged>());
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball, Components = [log] });
        physics.OnStep((context, output) => output.Bodies.Add(new BodyState(ball, station, ReportedPose, ReportedVelocity, ReportedSpin)));

        test.Run(3);

        test.World.GetFrame(ball).Should().Be(station);
        log.Seen.Should().ContainSingle("the move is told once, though physics reports the body in the station every step");
    }

    [Fact]
    public void WhenPhysicsReportsOneBodyTwiceInAStepTheLaterStateWins()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        physics.OnStep((context, output) =>
        {
            output.Bodies.Add(new BodyState(ball, test.Root, TestWorld.At(1), Vector3.Zero, Vector3.Zero));
            output.Bodies.Add(new BodyState(ball, test.Root, TestWorld.At(2), Vector3.Zero, Vector3.Zero));
        });

        test.Run(1);

        test.World.GetPose(ball).Position.X.Should().Be(2);
    }

    // Physics hears of every destroyed entity and frame before its next step, and an entity keeps its body for life. So
    // a body state names a live entity with a body and a live frame, unless the backend has a bug.
#if TERTIUS_CONTRACTS
    [Fact]
    public void ABodyStateForAnEntityWithoutABodyBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity bodiless = test.SpawnAt(new Double3(3, 0, 0));
        physics.OnStep((context, output) => output.Bodies.Add(new BodyState(bodiless, test.Root, ReportedPose, ReportedVelocity, ReportedSpin)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void ABodyStateForAnEntityThatNoLongerExistsBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity gone = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        test.World.Destroy(gone);
        physics.OnStep((context, output) => output.Bodies.Add(new BodyState(gone, test.Root, ReportedPose, ReportedVelocity, ReportedSpin)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void ABodyStateThatNamesNoFrameBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        physics.OnStep((context, output) => output.Bodies.Add(new BodyState(ball, FrameHandle.Null, ReportedPose, ReportedVelocity, ReportedSpin)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }
#else
    [Fact]
    public void ABodyStateForAnEntityWithoutABodyIsReportedOnceAndChangesNothing()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity bodiless = test.SpawnAt(new Double3(3, 0, 0));
        physics.OnStep((context, output) => output.Bodies.Add(new BodyState(bodiless, test.Root, ReportedPose, ReportedVelocity, ReportedSpin)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {bodiless}").And.Contain("has no body");
        test.World.GetPose(bodiless).Position.Should().Be(new Double3(3, 0, 0));
    }

    [Fact]
    public void ABodyStateForAnEntityThatNoLongerExistsIsReportedOnceAndChangesNothing()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity gone = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        Entity bystander = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        test.World.Destroy(gone);
        physics.OnStep((context, output) => output.Bodies.Add(new BodyState(gone, test.Root, ReportedPose, ReportedVelocity, ReportedSpin)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {gone}").And.Contain("no longer exists");
        test.World.GetPose(bystander).Position.X.Should().Be(3);
    }

    [Fact]
    public void ABodyStateWithAStaleHandleDoesNotMoveTheEntityThatReusedTheSlot()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity gone = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        test.World.Destroy(gone);
        Entity successor = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        successor.Index.Should().Be(gone.Index, "the test needs the successor to sit in the slot of the entity that was destroyed");
        physics.OnStep((context, output) => output.Bodies.Add(new BodyState(gone, test.Root, ReportedPose, ReportedVelocity, ReportedSpin)));

        test.Run(1);

        test.World.GetPose(successor).Position.X.Should().Be(3);
        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {gone}");
    }

    [Fact]
    public void EachBrokenOutputAboutOneBodyIsReportedOnce()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle gone = test.SpawnStaticFrame(TestWorld.At(500), radius: 10);
        test.World.Destroy(gone.Entity);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        physics.OnStep((context, output) =>
        {
            output.Bodies.Add(new BodyState(ball, gone, ReportedPose, ReportedVelocity, ReportedSpin));
            output.Bodies.Add(new BodyState(ball, FrameHandle.Null, ReportedPose, ReportedVelocity, ReportedSpin));
        });

        test.Run(1);

        test.World.Errors.Should().HaveCount(2).And.OnlyContain(error => error.Contains($"body state of {ball}"));
        test.World.GetPose(ball).Should().Be(TestWorld.At(3));
    }

    [Fact]
    public void ABodyStateThatNamesNoFrameIsReportedOnceAndChangesNothing()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = station, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        physics.OnStep((context, output) => output.Bodies.Add(new BodyState(ball, FrameHandle.Null, ReportedPose, ReportedVelocity, ReportedSpin)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {ball}").And.Contain("names no frame");
        test.World.GetFrame(ball).Should().Be(station);
        test.World.GetPose(ball).Should().Be(TestWorld.At(3));
    }
#endif

    [Fact]
    public void AnIntegratingBackendMovesABodyByItsVelocityAndTheResultReachesTheSnapshot()
    {
        // At 64 ticks per second a step is exactly 1/64 s, so three steps at 8 m/s move the ball 24/64 = 0.375 m exactly.
        var physics = new FakePhysics();
        using var test = new TestWorld(physics, configure: options => options.TickRate = 64);
        Entity ball = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(1),
            LinearVelocity = new Vector3(8, 0, 0),
            Body = TestWorld.Ball,
        });

        test.Run(3);

        test.World.GetPose(ball).Position.Should().Be(new Double3(1.375, 0, 0));
        test.World.GetVelocity(ball).Should().Be(new Vector3(8, 0, 0));
    }

    [Fact]
    public void WithoutAPhysicsBackendABodyStaysWhereItWasSpawnedAndNoErrorIsReported()
    {
        using var test = new TestWorld();
        Entity ball = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(2),
            LinearVelocity = new Vector3(5, 0, 0),
            Body = TestWorld.Ball,
        });

        test.Run(5);

        test.World.GetPose(ball).Position.X.Should().Be(2);
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void WithoutAPhysicsBackendTheIntentsOfOneTickDoNotPileUpIntoTheNext()
    {
        using var test = new TestWorld();
        var pendingAtTheStartOfEachTick = new List<float>();
        test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Body = TestWorld.Ball,
            Components = [Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                pendingAtTheStartOfEachTick.Add(self.Body.Pending.Force.Y);
                self.Body.AddForce(Vector3.UnitY);
            })],
        });

        test.Run(3);

        test.World.Errors.Should().BeEmpty();
        pendingAtTheStartOfEachTick.Should().Equal(0, 0, 0);
    }
}
