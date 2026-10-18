using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A hand-off is physics telling the world that a body left one frame for another during a step. The world applies it at
/// the sync point: the entity is in the new frame with the pose and velocities that physics reports, it hears
/// <see cref="FrameChanged"/> once, and it leaves the member set of the old frame's topic for the new one's. The world
/// runs on real Jolt. A test of a valid hand-off flies a ball into the bubble of a frame, and checks the world against the
/// hand-off that Jolt really sent. A test of a hand-off that only a bug produces edits the output of one step, through
/// <see cref="FaultInjectingBackend"/>.
/// </summary>
public class PhysicsHandOffTests
{
    private static readonly Vector3 AnyVelocity = new(1, 2, 3);
    private static readonly Vector3 AnySpin = new(0, 4, 0);

    /// <summary>
    /// A ball with a body in the root frame and a station frame 500 m from the origin of the root. The ball spins, and
    /// flies at the station so that Jolt hands it off to the station in the step of <c>handOffOnTick</c>. The ball keeps a
    /// <see cref="Recorder"/> for the messages that the tests look at.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        private readonly long handOffOnTick;

        public Scene(long handOffOnTick = 0)
        {
            this.handOffOnTick = handOffOnTick;
            Test = TestWorld.OnJolt(out FaultInjectingBackend faults);
            Faults = faults;
            Station = Test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
            BallLog = new Recorder(EventMask.Of<FrameChanged>().And<Ping>());
            Scenes.Approach flight = Scenes.ApproachToBubble(Test.World, Station, handOffOnTick);
            Ball = Test.World.Spawn(new SpawnDesc
            {
                Frame = Test.Root,
                Pose = flight.Pose,
                LinearVelocity = flight.Velocity,
                AngularVelocity = AnySpin,
                Body = WorldTesting.Ball(),
                Components = [BallLog],
            });
        }

        public FaultInjectingBackend Faults { get; }
        public TestWorld Test { get; }
        public FrameHandle Station { get; }
        public Entity Ball { get; }
        public Recorder BallLog { get; }
        public World World => Test.World;

        /// <summary>The hand-off of the ball that Jolt sent. The ball enters the station once.</summary>
        public HandOff HandOff => Faults.Outputs[(int)handOffOnTick].HandOffs.Should().ContainSingle().Subject;

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void AHandOffPutsTheEntityInTheNewFrame()
    {
        using var scene = new Scene();

        scene.Test.Run(1);

        scene.HandOff.Entity.Should().Be(scene.Ball);
        scene.World.GetFrame(scene.Ball).Should().Be(scene.Station);
    }

    [Fact]
    public void AHandOffGivesTheEntityThePoseAndVelocitiesThatPhysicsReports()
    {
        using var scene = new Scene();

        scene.Test.Run(1);

        HandOff handOff = scene.HandOff;
        PoseChecks.AssertNear(handOff.LinearVelocity, new Vector3(12, 0, 0));
        PoseChecks.AssertNear(handOff.AngularVelocity, AnySpin);
        scene.World.PoseInOwnFrame(scene.Ball).Should().Be(handOff.Pose);
        scene.World.VelocityInOwnFrame(scene.Ball).Should().Be(handOff.LinearVelocity);
        scene.World.AngularVelocityInOwnFrame(scene.Ball).Should().Be(handOff.AngularVelocity);
    }

    [Fact]
    public void AnEntityStaysInItsOldFrameAsLongAsPhysicsReportsNoHandOff()
    {
        using var scene = new Scene(handOffOnTick: 1);

        scene.Test.Run(1);

        scene.Faults.Outputs.Should().ContainSingle().Which.HandOffs.Should().BeEmpty();
        BodyState reported = scene.Faults.Outputs[0].Bodies.Single(state => state.Entity == scene.Ball);
        scene.World.GetFrame(scene.Ball).Should().Be(scene.Test.Root);
        scene.World.PoseInOwnFrame(scene.Ball).Should().Be(reported.Pose);
    }

    [Fact]
    public void AnEntityHearsFrameChangedOnceAfterItsHandOffWithTheOldAndTheNewFrame()
    {
        using var scene = new Scene();

        scene.Test.Run(4);

        // The sync point of tick 0 sends it, so the ball handles it during tick 1.
        (long tick, Event received) = scene.BallLog.Seen.Should().ContainSingle(s => s.Event.Is<FrameChanged>()).Subject;
        tick.Should().Be(1);
        received.As<FrameChanged>().From.Should().Be(scene.Test.Root);
        received.As<FrameChanged>().To.Should().Be(scene.Station);
    }

    [Fact]
    public void AnEntityHearsTheTopicOfItsNewFrameAfterTheHandOff()
    {
        using var scene = new Scene();
        // Each tick, one sender tells each frame's topic the tick. A message sent in tick t reaches its recipients in tick t + 1.
        scene.Test.SpawnWith(SendEvery.Of<Ping>(scene.Station.Topic, tick => new Ping { Value = 200 + (int)tick }));

        scene.Test.Run(4);

        // The hand-off happens at the sync point of tick 0, before that tick's messages are routed, so the ball is
        // a member of the station already when the first message is routed.
        scene.BallLog.Pings().Should().Equal((1L, 200), (2L, 201), (3L, 202));
    }

    [Fact]
    public void AnEntityNoLongerHearsTheTopicOfItsOldFrameAfterTheHandOff()
    {
        using var scene = new Scene(handOffOnTick: 1);
        scene.Test.SpawnWith(SendEvery.Of<Ping>(scene.Test.Root.Topic, tick => new Ping { Value = 100 + (int)tick }));

        scene.Test.Run(4);

        // Sent in tick 0, routed before the hand-off of tick 1: the ball is still in the root and hears it in tick 1.
        // Sent in tick 1 and later, routed after the hand-off: the ball is in the station and hears nothing.
        scene.BallLog.Pings().Should().Equal((1L, 100));
    }

    [Fact]
    public void AnEntityHearsItsOldFramesTopicUntilTheSyncPointOfTheHandOffAndNotFromThenOn()
    {
        using var scene = new Scene(handOffOnTick: 2);
        scene.Test.SpawnWith(SendEvery.Of<Ping>(scene.Test.Root.Topic, tick => new Ping { Value = 100 + (int)tick }));

        scene.Test.Run(5);

        // Sent in ticks 0 and 1, routed while the ball is still in the root. Sent in tick 2: the sync point of tick 2
        // applies the hand-off (step 2) before it routes messages (step 8), so the ball no longer hears it.
        scene.BallLog.Pings().Should().Equal((1L, 100), (2L, 101));
    }

    // The fighter starts inside the bubble of the carrier, so Jolt hands it off to the carrier in the first step.
    [Fact]
    public void AHandOffOfAFrameOwnerMovesTheFrameUnderTheNewParent()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(1100), out FrameHandle fighter);

        test.Run(1);

        HandOff handOff = faults.Outputs[0].HandOffs.Should().ContainSingle().Subject;
        handOff.Entity.Should().Be(fighterOwner);
        handOff.To.Should().Be(carrier);
        test.World.Frames.Parent(fighter).Should().Be(carrier);
        test.World.GetFrame(fighterOwner).Should().Be(carrier);
        PoseChecks.AssertNear(test.World.PoseInOwnFrame(fighterOwner).Position, new Double3(100, 0, 0));
    }

    [Fact]
    public void ThePassengersOfAFrameOwnerThatWasHandedOffStayInTheFrameAndRideAlong()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(1100), out FrameHandle fighter);
        Entity pilot = test.World.SpawnAt(fighter, new Double3(0, 1, 0));

        test.Run(1);

        faults.Outputs[0].HandOffs.Should().ContainSingle().Which.To.Should().Be(carrier);
        test.World.GetFrame(pilot).Should().Be(fighter);
        test.World.Snapshot.TryGetPoseIn(pilot, test.Root, out DPose pilotInRoot).Should().BeTrue();
        // The carrier at 1000 m, the fighter 100 m inside it, and the pilot 1 m above the fighter's origin.
        PoseChecks.AssertNear(pilotInRoot.Position, new Double3(1100, 1, 0));
    }

    // Physics hears of every destroyed entity and frame before its next step, and an entity keeps its body for life, so it
    // hands off only live entities with bodies, into frames that the tree has. It follows the frame tree, so a hand-off
    // leaves the frame that the entity is in, never the frame that it enters, and it never puts a frame inside itself. A
    // hand-off that breaks any of these rules comes only from a bug in the backend.
#if TERTIUS_CONTRACTS
    [Fact]
    public void AHandOffOfAnEntityThatNoLongerExistsBreaksAContract()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity gone = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        test.World.Destroy(gone);
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(gone, test.Root, station, TestWorld.At(9), default, default));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AHandOffOfAnEntityWithoutABodyBreaksAContract()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity bodiless = test.SpawnAt(new Double3(3, 0, 0));
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(bodiless, test.Root, station, TestWorld.At(9), default, default));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AHandOffToAFrameThatIsGoneBreaksAContract()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle gone = test.SpawnStaticFrame(TestWorld.At(0), radius: 10);
        test.World.Destroy(gone.Entity);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(ball, test.Root, gone, TestWorld.At(9), default, default));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AHandOffFromAFrameThatTheEntityIsNotInBreaksAContract()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        FrameHandle dock = test.SpawnStaticFrame(TestWorld.At(-500), radius: 100);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(ball, dock, station, TestWorld.At(9), default, default));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{ball} is in {test.Root}, not in {dock}*");
    }

    [Fact]
    public void AHandOffOfTheOwnerOfARootFrameBreaksAContract()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity islandOwner = SpawnRootWithBody(test);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(islandOwner, test.Root, station, TestWorld.At(9), default, default));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{islandOwner} is in no frame*");
    }

    [Fact]
    public void AHandOffIntoTheFrameThatTheEntityIsInBreaksAContract()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(ball, test.Root, test.Root, TestWorld.At(9), default, default));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void ASecondHandOffOfOneEntityInOneStepBreaksAContract()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        FrameHandle dock = test.SpawnStaticFrame(TestWorld.At(-500), radius: 100);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        faults.EditOutput = output =>
        {
            output.HandOffs.Add(new HandOff(ball, test.Root, station, TestWorld.At(9), default, default));
            output.HandOffs.Add(new HandOff(ball, test.Root, dock, TestWorld.At(9), default, default));
        };

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{ball} has another hand-off in this step*");
    }

    [Fact]
    public void TwoShipsThatAStepHandsIntoEachOtherBreakAContract()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity first = SpawnFighter(test, TestWorld.At(0), out FrameHandle firstFrame);
        Entity second = SpawnFighter(test, TestWorld.At(100), out FrameHandle secondFrame);
        faults.EditOutput = output =>
        {
            output.HandOffs.Add(new HandOff(first, test.Root, secondFrame, TestWorld.At(-100), default, default));
            output.HandOffs.Add(new HandOff(second, test.Root, firstFrame, TestWorld.At(100), default, default));
        };

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*refused the hand-off of {second}*");
    }

    [Fact]
    public void AHandOffThatTheFrameTreeRefusesBreaksAContract()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(0), out FrameHandle fighter);
        FrameHandle cockpit = test.World.SpawnStaticFrame(fighter, TestWorld.At(1), radius: 2);
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(fighterOwner, test.Root, cockpit, TestWorld.At(0), default, default));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    // Physics moves a frame only through the hull of a frame with the physics driver. The hull of a static frame is static
    // and the hull of a Kepler frame is kinematic, and physics hands neither of them off.
    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Kepler)]
    public void AHandOffOfTheOwnerOfAFrameThatPhysicsDoesNotDriveBreaksAContract(MotionDriver driver)
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity owner = SpawnFrameWithHull(test, driver, out FrameHandle frame);
        FrameHandle yard = test.SpawnStaticFrame(TestWorld.At(5), radius: 100);
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(owner, test.Root, yard, TestWorld.At(-5), default, default));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*hand-off of {owner}*physics does not drive {frame}*");
    }
#else
    [Fact]
    public void AHandOffOfAnEntityThatNoLongerExistsIsReportedOnceAndChangesNothing()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity gone = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        Entity bystander = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(7), Body = TestWorld.Ball });
        test.World.Destroy(gone);
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(gone, test.Root, station, TestWorld.At(9), default, default));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {gone}").And.Contain("no longer exists");
        test.World.GetFrame(bystander).Should().Be(test.Root);
        test.World.PoseInOwnFrame(bystander).Position.X.Should().Be(7);
    }

    [Fact]
    public void AHandOffOfAnEntityWithoutABodyIsReportedOnceAndLeavesTheEntityWhereItWas()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity bodiless = test.SpawnAt(new Double3(3, 0, 0));
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(bodiless, test.Root, station, TestWorld.At(9), default, default));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {bodiless}").And.Contain("has no body");
        test.World.GetFrame(bodiless).Should().Be(test.Root);
        test.World.PoseInOwnFrame(bodiless).Position.Should().Be(new Double3(3, 0, 0));
    }

    [Fact]
    public void AHandOffToAStaleFrameIsReportedAndTheEntityStaysWhereItWas()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle gone = test.SpawnStaticFrame(TestWorld.At(0), radius: 10);
        test.World.Destroy(gone.Entity);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(ball, test.Root, gone, TestWorld.At(9), default, default));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain("stale frame");
        test.World.GetFrame(ball).Should().Be(test.Root);
        test.World.PoseInOwnFrame(ball).Position.X.Should().Be(3);
    }

    [Fact]
    public void AHandOffToAStaleFrameAlsoIgnoresTheStatesThatPhysicsReportsRelativeToThatFrame()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(100), out FrameHandle fighter);
        FrameHandle gone = test.SpawnStaticFrame(TestWorld.At(0), radius: 10);
        test.World.Destroy(gone.Entity);
        // What the Jolt backend reports with a hand-off: the body state and the frame state in the target frame.
        faults.EditNextOutput = output =>
        {
            output.Bodies.Add(new BodyState(fighterOwner, gone, TestWorld.At(-1), AnyVelocity, AnySpin));
            output.HandOffs.Add(new HandOff(fighterOwner, test.Root, gone, TestWorld.At(-1), AnyVelocity, AnySpin));
            output.Frames.Add(StepRecords.Of(fighter, TestWorld.At(-1), new Double3(1, 2, 3), AnySpin, Vector3.Zero));
        };

        test.Run(1);

        // Two outputs name the stale frame, the body state and the hand-off, and each is reported. The frame state is
        // sound, but it belongs to the skipped update.
        test.World.Errors.Should().HaveCount(2).And.OnlyContain(error => error.Contains("stale frame"));
        test.World.Frames.LocalPose(fighter).Should().Be(TestWorld.At(100));
        test.World.PoseInOwnFrame(fighterOwner).Should().Be(TestWorld.At(100));
    }

    [Fact]
    public void ARefusedHandOffIsReportedAlsoWhenAnotherBrokenOutputSkippedTheOwnersUpdate()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(0), out FrameHandle fighter);
        FrameHandle cockpit = test.World.SpawnStaticFrame(fighter, TestWorld.At(1), radius: 2);
        FrameHandle gone = test.SpawnStaticFrame(TestWorld.At(500), radius: 10);
        test.World.Destroy(gone.Entity);
        faults.EditNextOutput = output =>
        {
            output.Bodies.Add(new BodyState(fighterOwner, gone, TestWorld.At(0), default, default));
            output.HandOffs.Add(new HandOff(fighterOwner, test.Root, cockpit, TestWorld.At(0), default, default));
        };

        test.Run(1);

        test.World.Errors.Should().HaveCount(2);
        test.World.Errors.Should().ContainSingle(error => error.Contains($"body state of {fighterOwner}"));
        test.World.Errors.Should().ContainSingle(error => error.Contains($"refused the hand-off of {fighterOwner}"));
        test.World.Frames.Parent(fighter).Should().Be(test.Root);
    }

    [Fact]
    public void AHandOffFromAFrameThatTheEntityIsNotInIsReportedOnceAndLeavesTheEntityWhereItWas()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        FrameHandle dock = test.SpawnStaticFrame(TestWorld.At(-500), radius: 100);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(ball, dock, station, TestWorld.At(9), default, default));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {ball}").And.Contain($"{ball} is in {test.Root}, not in {dock}");
        test.World.GetFrame(ball).Should().Be(test.Root);
        test.World.PoseInOwnFrame(ball).Should().Be(TestWorld.At(3));
    }

    [Fact]
    public void TheOwnerOfARootFrameNeverHandsItsFrameOff()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity islandOwner = SpawnRootWithBody(test);
        FrameHandle island = test.World.GetOwnedFrame(islandOwner);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        faults.EditOutputOfTick(0, output => output.HandOffs.Add(new HandOff(islandOwner, test.Root, station, TestWorld.At(9), default, default)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {islandOwner}").And.Contain($"{islandOwner} is in no frame");
        test.World.Frames.Parent(island).Should().Be(FrameHandle.Null, "a root frame stays a root");
        test.World.GetFrame(islandOwner).Should().Be(FrameHandle.Null);
        test.World.AssertInvariants();
    }

    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Kepler)]
    public void AHandOffOfTheOwnerOfAFrameThatPhysicsDoesNotDriveIsReportedOnceAndTheFrameStaysUnderItsParent(MotionDriver driver)
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity owner = SpawnFrameWithHull(test, driver, out FrameHandle frame);
        FrameHandle yard = test.SpawnStaticFrame(TestWorld.At(5), radius: 100);
        faults.EditOutputOfTick(0, output => output.HandOffs.Add(new HandOff(owner, test.Root, yard, TestWorld.At(-5), default, default)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {owner}").And.Contain($"physics does not drive {frame}");
        test.World.Frames.Parent(frame).Should().Be(test.Root);
        test.World.GetFrame(owner).Should().Be(test.Root);
        test.World.AssertInvariants();
    }

    [Fact]
    public void AHandOffIntoTheFrameThatTheEntityIsInIsReportedOnceAndChangesNothing()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        faults.EditOutput = output => output.HandOffs.Add(new HandOff(ball, test.Root, test.Root, TestWorld.At(9), default, default));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {ball}").And.Contain($"into {test.Root}, which it is in already");
        test.World.PoseInOwnFrame(ball).Should().Be(TestWorld.At(3));
    }

    [Fact]
    public void ASecondHandOffOfOneEntityInOneStepIsReportedAndTheEntityStaysWhereItWas()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        FrameHandle dock = test.SpawnStaticFrame(TestWorld.At(-500), radius: 100);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        faults.EditNextOutput = output =>
        {
            output.HandOffs.Add(new HandOff(ball, test.Root, station, TestWorld.At(9), default, default));
            output.HandOffs.Add(new HandOff(ball, test.Root, dock, TestWorld.At(9), default, default));
        };

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {ball}").And.Contain("another hand-off in this step");
        test.World.GetFrame(ball).Should().Be(test.Root, "the first hand-off is part of the skipped update");
        test.World.PoseInOwnFrame(ball).Should().Be(TestWorld.At(3));
    }

    [Fact]
    public void OfTwoShipsThatAStepHandsIntoEachOtherTheFirstMovesAndTheSecondIsRefused()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity first = SpawnFighter(test, TestWorld.At(0), out FrameHandle firstFrame);
        Entity second = SpawnFighter(test, TestWorld.At(100), out FrameHandle secondFrame);
        faults.EditNextOutput = output =>
        {
            output.HandOffs.Add(new HandOff(first, test.Root, secondFrame, TestWorld.At(-100), default, default));
            output.HandOffs.Add(new HandOff(second, test.Root, firstFrame, TestWorld.At(100), default, default));
        };

        test.Run(1);

        // The hand-offs apply in their order: the first ship enters the second, so the second cannot enter the first.
        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"refused the hand-off of {second}");
        test.World.Frames.Parent(firstFrame).Should().Be(secondFrame);
        test.World.Frames.Parent(secondFrame).Should().Be(test.Root);
        test.World.AssertInvariants();
    }

    [Fact]
    public void AHandOffThatASkippedHandOffWouldHaveRefusedGoesAhead()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity first = SpawnFighter(test, TestWorld.At(0), out FrameHandle firstFrame);
        Entity second = SpawnFighter(test, TestWorld.At(100), out FrameHandle secondFrame);
        FrameHandle gone = test.SpawnStaticFrame(TestWorld.At(500), radius: 10);
        test.World.Destroy(gone.Entity);
        faults.EditNextOutput = output =>
        {
            // The body state skips the first ship's whole update, so its hand-off moves nothing.
            output.Bodies.Add(new BodyState(first, gone, TestWorld.At(0), default, default));
            output.HandOffs.Add(new HandOff(first, test.Root, secondFrame, TestWorld.At(-100), default, default));
            output.HandOffs.Add(new HandOff(second, test.Root, firstFrame, TestWorld.At(100), default, default));
        };

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"body state of {first}");
        test.World.Frames.Parent(firstFrame).Should().Be(test.Root);
        test.World.Frames.Parent(secondFrame).Should().Be(firstFrame);
        test.World.AssertInvariants();
    }

    [Fact]
    public void AHandOffOfAFrameOwnerIntoItsOwnSubframeIsReportedAndTheOwnerStaysInItsFrame()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(0), out FrameHandle fighter);
        FrameHandle cockpit = test.World.SpawnStaticFrame(fighter, TestWorld.At(1), radius: 2);
        faults.EditOutputOfTick(0, output => output.HandOffs.Add(new HandOff(fighterOwner, test.Root, cockpit, TestWorld.At(0), default, default)));

        test.Run(1);

        // A frame cannot be its own descendant's child.
        test.World.Errors.Should().ContainSingle().Which.Should().Contain("refused the hand-off");
        test.World.Frames.Parent(fighter).Should().Be(test.Root);
        test.World.GetFrame(fighterOwner).Should().Be(test.Root);
    }

    [Fact]
    public void AHandOffThatTheFrameTreeRefusesSendsNoFrameChanged()
    {
        using var test = TestWorld.OnJolt(out FaultInjectingBackend faults);
        var log = new Recorder(EventMask.Of<FrameChanged>());
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(0), out FrameHandle fighter, log);
        FrameHandle cockpit = test.World.SpawnStaticFrame(fighter, TestWorld.At(1), radius: 2);
        faults.EditOutputOfTick(0, output => output.HandOffs.Add(new HandOff(fighterOwner, test.Root, cockpit, TestWorld.At(0), default, default)));

        test.Run(3);

        log.Seen.Should().BeEmpty("the owner never left the root frame");
    }

    /// <summary>What a physics step reports together with a hand-off that the frame tree refuses.</summary>
    public enum RefusedReport
    {
        /// <summary>The owner's body state in the refused target frame.</summary>
        BodyState,
        /// <summary>
        /// The owner's body state in the refused target frame, and the owned frame's state relative to that target:
        /// what the Jolt backend reports for every hand-off that it executes.
        /// </summary>
        BodyAndFrameState,
    }

    /// <summary>
    /// A fighter whose frame origin is 100 m along +X from the origin of the root, moving and spinning, with a cockpit
    /// frame 1 m along +X inside it. The first physics step hands the fighter's owner into its own cockpit, which the
    /// frame tree refuses, and reports with it what <see cref="RefusedReport"/> says. Seen from the cockpit, the fighter's
    /// origin is at X = -1.
    /// </summary>
    private sealed class RefusedHandOffScene : IDisposable
    {
        public static readonly Vector3 OwnVelocity = new(0, 0, 5);
        public static readonly Vector3 OwnSpin = new(0, 0.25f, 0);

        public RefusedHandOffScene(RefusedReport report, params IComponent[] ownerComponents)
        {
            Test = TestWorld.OnJolt(out FaultInjectingBackend faults);
            Owner = Test.World.Spawn(new SpawnDesc
            {
                Frame = Test.Root,
                Pose = TestWorld.At(100),
                LinearVelocity = OwnVelocity,
                AngularVelocity = OwnSpin,
                AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
                Body = new BodyDesc(new SphereShapeDesc(5), Layer: BodyLayer.FrameHull),
                Components = ownerComponents,
            });
            Fighter = Test.World.GetOwnedFrame(Owner);
            Cockpit = Test.World.SpawnStaticFrame(Fighter, TestWorld.At(1), radius: 2);
            faults.EditNextOutput = output =>
            {
                output.Bodies.Add(new BodyState(Owner, Cockpit, TestWorld.At(-1), AnyVelocity, AnySpin));
                output.HandOffs.Add(new HandOff(Owner, Test.Root, Cockpit, TestWorld.At(-1), AnyVelocity, AnySpin));
                if (report == RefusedReport.BodyAndFrameState)
                    output.Frames.Add(StepRecords.Of(Fighter, TestWorld.At(-1), new Double3(1, 2, 3), AnySpin, new Vector3(0, -2, 0)));
            };
        }

        public TestWorld Test { get; }
        public Entity Owner { get; }
        public FrameHandle Fighter { get; }
        public FrameHandle Cockpit { get; }
        public World World => Test.World;

        public void Dispose() => Test.Dispose();
    }

    [Theory]
    [InlineData(RefusedReport.BodyState)]
    [InlineData(RefusedReport.BodyAndFrameState)]
    public void TheOwnerOfARefusedHandOffHearsNoFrameChanged(RefusedReport report)
    {
        var log = new Recorder(EventMask.Of<FrameChanged>());
        using var scene = new RefusedHandOffScene(report, log);

        scene.Test.Run(3);

        log.Seen.Should().BeEmpty("the owner never left the root frame");
    }

    [Theory]
    [InlineData(RefusedReport.BodyState)]
    [InlineData(RefusedReport.BodyAndFrameState)]
    public void TheOwnerOfARefusedHandOffKeepsItsFramePoseAndVelocities(RefusedReport report)
    {
        using var scene = new RefusedHandOffScene(report);

        scene.Test.Run(1);

        scene.World.GetFrame(scene.Owner).Should().Be(scene.Test.Root);
        scene.World.PoseInOwnFrame(scene.Owner).Should().Be(TestWorld.At(100));
        scene.World.VelocityInOwnFrame(scene.Owner).Should().Be(RefusedHandOffScene.OwnVelocity);
        scene.World.AngularVelocityInOwnFrame(scene.Owner).Should().Be(RefusedHandOffScene.OwnSpin);
    }

    [Theory]
    [InlineData(RefusedReport.BodyState)]
    [InlineData(RefusedReport.BodyAndFrameState)]
    public void TheFrameOfARefusedHandOffKeepsItsParentLocalPoseAndMotion(RefusedReport report)
    {
        using var scene = new RefusedHandOffScene(report);

        scene.Test.Run(1);

        FrameTree frames = scene.World.Frames;
        frames.Parent(scene.Fighter).Should().Be(scene.Test.Root);
        frames.LocalPose(scene.Fighter).Should().Be(TestWorld.At(100));
        frames.LinearVelocity(scene.Fighter).Should().Be((Double3)RefusedHandOffScene.OwnVelocity);
        frames.AngularVelocity(scene.Fighter).Should().Be(RefusedHandOffScene.OwnSpin);
        frames.ProperAcceleration(scene.Fighter).Should().Be(Vector3.Zero);
    }

    [Theory]
    [InlineData(RefusedReport.BodyState)]
    [InlineData(RefusedReport.BodyAndFrameState)]
    public void TheOwnerOfARefusedHandOffStaysAMemberOfItsFramesTopic(RefusedReport report)
    {
        var log = new Recorder(EventMask.Of<Ping>());
        using var scene = new RefusedHandOffScene(report, log);
        // Each tick, one sender tells each frame's topic the tick. A message sent in tick t reaches its recipients in tick t + 1.
        scene.Test.SpawnWith(SendEvery.Of<Ping>(scene.Test.Root.Topic, tick => new Ping { Value = 100 + (int)tick }));
        scene.Test.SpawnWith(SendEvery.Of<Ping>(scene.Cockpit.Topic, tick => new Ping { Value = 200 + (int)tick }));

        scene.Test.Run(3);

        log.Pings().Should().Equal((1L, 100), (2L, 101));
    }

    [Theory]
    [InlineData(RefusedReport.BodyState)]
    [InlineData(RefusedReport.BodyAndFrameState)]
    public void ARefusedHandOffIsReportedOnceWithTheEntityAndTheRefusedMove(RefusedReport report)
    {
        using var scene = new RefusedHandOffScene(report);

        scene.Test.Run(1);

        string error = scene.World.Errors.Should().ContainSingle().Subject;
        error.Should().Contain($"refused the hand-off of {scene.Owner} from {scene.Test.Root} to {scene.Cockpit}");
    }

#endif

    /// <summary>
    /// A second root frame, an island of radius 100 m, whose owner has a body: only the static interior in its own frame,
    /// because a root frame has no parent for a hull.
    /// </summary>
    private static Entity SpawnRootWithBody(TestWorld test) => test.World.Spawn(new SpawnDesc
    {
        AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static),
        Body = new BodyDesc(new SphereShapeDesc(5), BodyMotion.Static),
    });

    /// <summary>
    /// A frame of radius 10 m near the origin of the root, with <paramref name="driver"/>, whose owner has a hull in the root:
    /// a static body for the static driver, a kinematic one for the Kepler driver. A Kepler frame circles the origin at 1 m.
    /// </summary>
    private static Entity SpawnFrameWithHull(TestWorld test, MotionDriver driver, out FrameHandle frame)
    {
        // The Kepler frame takes its G·M from the point gravity of the root: 1 m³/s², so that it circles at 1 m in 2π s.
        if (driver == MotionDriver.Kepler)
            test.GiveRootPointGravity(1);
        Entity owner = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), driver, Orbit: driver == MotionDriver.Kepler ? KeplerOrbit.Circular(1) : default),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), driver == MotionDriver.Kepler ? BodyMotion.Kinematic : BodyMotion.Static),
        });
        frame = test.World.GetOwnedFrame(owner);
        return owner;
    }

    /// <summary>A small ship: a frame that physics moves, whose owner has a hull body in the parent frame.</summary>
    private static Entity SpawnFighter(TestWorld test, DPose pose, out FrameHandle frame, params IComponent[] components)
    {
        Entity owner = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = pose,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
            Body = new BodyDesc(new SphereShapeDesc(5), Layer: BodyLayer.FrameHull),
            Components = components,
        });
        frame = test.World.GetOwnedFrame(owner);
        return owner;
    }
}
