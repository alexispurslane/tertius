using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A hand-off is physics telling the world that a body left one frame for another during a step. The world applies it at
/// the sync point: the entity is in the new frame with the pose and velocities that physics reports, it hears
/// <see cref="FrameChanged"/> once, and it leaves the member set of the old frame's topic for the new one's. A physics
/// double (an NSubstitute backend) reports the hand-off, so the tests decide exactly what physics said.
/// </summary>
public class PhysicsHandOffTests
{
    private static readonly Vector3 AnyVelocity = new(1, 2, 3);
    private static readonly Vector3 AnySpin = new(0, 4, 0);

    /// <summary>
    /// A ball with a body in the root frame, a station frame 500 m from the origin of the root, and a physics double
    /// whose first step hands the ball off to the station. The ball keeps a <see cref="Recorder"/> for the messages that the tests look at.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        public Scene(long handOffOnTick = 0)
        {
            Physics = TestPhysics.Create();
            Test = new TestWorld(Physics);
            Station = Test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
            BallLog = new Recorder(EventMask.Of<FrameChanged>().And<Ping>());
            Ball = Test.World.Spawn(new SpawnDesc { Frame = Test.Root, Pose = TestWorld.At(450), Body = TestWorld.Ball, Components = [BallLog] });
            Physics.OnStep((context, output) =>
            {
                if (context.Tick == handOffOnTick)
                    output.HandOffs.Add(new HandOff(Ball, Test.Root, Station, TestWorld.At(-50), AnyVelocity, AnySpin));
            });
        }

        public IPhysicsBackend Physics { get; }
        public TestWorld Test { get; }
        public FrameHandle Station { get; }
        public Entity Ball { get; }
        public Recorder BallLog { get; }
        public World World => Test.World;

        public void Dispose() => Test.Dispose();
    }

    [Fact]
    public void AHandOffPutsTheEntityInTheNewFrame()
    {
        using var scene = new Scene();

        scene.Test.Run(1);

        scene.World.GetFrame(scene.Ball).Should().Be(scene.Station);
    }

    [Fact]
    public void AHandOffGivesTheEntityThePoseAndVelocitiesThatPhysicsReports()
    {
        using var scene = new Scene();

        scene.Test.Run(1);

        scene.World.GetPose(scene.Ball).Should().Be(TestWorld.At(-50));
        scene.World.GetVelocity(scene.Ball).Should().Be(AnyVelocity);
        scene.World.GetAngularVelocity(scene.Ball).Should().Be(AnySpin);
    }

    [Fact]
    public void AnEntityStaysInItsOldFrameAsLongAsPhysicsReportsNoHandOff()
    {
        using var scene = new Scene(handOffOnTick: 1);

        scene.Test.Run(1);

        scene.World.GetFrame(scene.Ball).Should().Be(scene.Test.Root);
        scene.World.GetPose(scene.Ball).Should().Be(TestWorld.At(450));
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

    [Fact]
    public void AHandOffOfAFrameOwnerMovesTheFrameUnderTheNewParent()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(1100), out FrameHandle fighter);
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.HandOffs.Add(new HandOff(fighterOwner, test.Root, carrier, TestWorld.At(100), default, default));
        });

        test.Run(1);

        test.World.Frames.Parent(fighter).Should().Be(carrier);
        test.World.GetFrame(fighterOwner).Should().Be(carrier);
        PoseChecks.AssertNear(test.World.GetPose(fighterOwner).Position, new Double3(100, 0, 0));
    }

    [Fact]
    public void ThePassengersOfAFrameOwnerThatWasHandedOffStayInTheFrameAndRideAlong()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle carrier = test.SpawnStaticFrame(TestWorld.At(1000), radius: 500);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(1100), out FrameHandle fighter);
        Entity pilot = test.World.SpawnAt(fighter, new Double3(0, 1, 0));
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.HandOffs.Add(new HandOff(fighterOwner, test.Root, carrier, TestWorld.At(100), default, default));
        });

        test.Run(1);

        test.World.GetFrame(pilot).Should().Be(fighter);
        test.World.Snapshot.TryGetPoseIn(pilot, test.Root, out DPose pilotInRoot).Should().BeTrue();
        // The carrier at 1000 m, the fighter 100 m inside it, and the pilot 1 m above the fighter's origin.
        PoseChecks.AssertNear(pilotInRoot.Position, new Double3(1100, 1, 0));
    }

    // Physics hears of every destroyed entity and frame before its next step, and an entity keeps its body for life, so it
    // hands off only live entities with bodies, into frames that the tree has. It follows the frame tree, so it never
    // puts a frame inside itself. A hand-off that breaks any of these rules comes only from a bug in the backend.
#if TERTIUS_CONTRACTS
    [Fact]
    public void AHandOffOfAnEntityThatNoLongerExistsBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity gone = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        test.World.Destroy(gone);
        physics.OnStep((context, output) => output.HandOffs.Add(new HandOff(gone, test.Root, station, TestWorld.At(9), default, default)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AHandOffOfAnEntityWithoutABodyBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity bodiless = test.SpawnAt(new Double3(3, 0, 0));
        physics.OnStep((context, output) => output.HandOffs.Add(new HandOff(bodiless, test.Root, station, TestWorld.At(9), default, default)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AHandOffToAFrameThatIsGoneBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle gone = test.SpawnStaticFrame(TestWorld.At(0), radius: 10);
        test.World.Destroy(gone.Entity);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        physics.OnStep((context, output) => output.HandOffs.Add(new HandOff(ball, test.Root, gone, TestWorld.At(9), default, default)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }

    [Fact]
    public void AHandOffThatTheFrameTreeRefusesBreaksAContract()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(0), out FrameHandle fighter);
        FrameHandle cockpit = test.World.SpawnStaticFrame(fighter, TestWorld.At(1), radius: 2);
        physics.OnStep((context, output) => output.HandOffs.Add(new HandOff(fighterOwner, test.Root, cockpit, TestWorld.At(0), default, default)));

        Action tick = () => test.World.Tick();

        tick.Should().Throw<ContractException>();
    }
#else
    [Fact]
    public void AHandOffOfAnEntityThatNoLongerExistsIsReportedOnceAndChangesNothing()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity gone = test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        Entity bystander = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(7), Body = TestWorld.Ball });
        test.World.Destroy(gone);
        physics.OnStep((context, output) => output.HandOffs.Add(new HandOff(gone, test.Root, station, TestWorld.At(9), default, default)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {gone}").And.Contain("no longer exists");
        test.World.GetFrame(bystander).Should().Be(test.Root);
        test.World.GetPose(bystander).Position.X.Should().Be(7);
    }

    [Fact]
    public void AHandOffOfAnEntityWithoutABodyIsReportedOnceAndLeavesTheEntityWhereItWas()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle station = test.SpawnStaticFrame(TestWorld.At(500), radius: 100);
        Entity bodiless = test.SpawnAt(new Double3(3, 0, 0));
        physics.OnStep((context, output) => output.HandOffs.Add(new HandOff(bodiless, test.Root, station, TestWorld.At(9), default, default)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain($"hand-off of {bodiless}").And.Contain("has no body");
        test.World.GetFrame(bodiless).Should().Be(test.Root);
        test.World.GetPose(bodiless).Position.Should().Be(new Double3(3, 0, 0));
    }

    [Fact]
    public void AHandOffToAStaleFrameIsReportedAndTheEntityStaysWhereItWas()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        FrameHandle gone = test.SpawnStaticFrame(TestWorld.At(0), radius: 10);
        test.World.Destroy(gone.Entity);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(3), Body = TestWorld.Ball });
        physics.OnStep((context, output) => output.HandOffs.Add(new HandOff(ball, test.Root, gone, TestWorld.At(9), default, default)));

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain("stale frame");
        test.World.GetFrame(ball).Should().Be(test.Root);
        test.World.GetPose(ball).Position.X.Should().Be(3);
    }

    [Fact]
    public void AHandOffToAStaleFrameAlsoIgnoresTheStatesThatPhysicsReportsRelativeToThatFrame()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(100), out FrameHandle fighter);
        FrameHandle gone = test.SpawnStaticFrame(TestWorld.At(0), radius: 10);
        test.World.Destroy(gone.Entity);
        // What the Jolt backend reports with a hand-off: the body state and the frame state in the target frame.
        physics.OnStep((context, output) =>
        {
            if (context.Tick != 0)
                return;
            output.Bodies.Add(new BodyState(fighterOwner, gone, TestWorld.At(-1), AnyVelocity, AnySpin));
            output.HandOffs.Add(new HandOff(fighterOwner, test.Root, gone, TestWorld.At(-1), AnyVelocity, AnySpin));
            output.Frames.Add(new FrameState(fighter, TestWorld.At(-1), new Double3(1, 2, 3), AnySpin, Vector3.Zero));
        });

        test.Run(1);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain("stale frame");
        test.World.Frames.LocalPose(fighter).Should().Be(TestWorld.At(100));
        test.World.GetPose(fighterOwner).Should().Be(TestWorld.At(100));
    }

    [Fact]
    public void AHandOffOfAFrameOwnerIntoItsOwnSubframeIsReportedAndTheOwnerStaysInItsFrame()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(0), out FrameHandle fighter);
        FrameHandle cockpit = test.World.SpawnStaticFrame(fighter, TestWorld.At(1), radius: 2);
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.HandOffs.Add(new HandOff(fighterOwner, test.Root, cockpit, TestWorld.At(0), default, default));
        });

        test.Run(1);

        // A frame cannot be its own descendant's child.
        test.World.Errors.Should().ContainSingle().Which.Should().Contain("refused the hand-off");
        test.World.Frames.Parent(fighter).Should().Be(test.Root);
        test.World.GetFrame(fighterOwner).Should().Be(test.Root);
    }

    [Fact]
    public void AHandOffThatTheFrameTreeRefusesSendsNoFrameChanged()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        var log = new Recorder(EventMask.Of<FrameChanged>());
        Entity fighterOwner = SpawnFighter(test, TestWorld.At(0), out FrameHandle fighter, log);
        FrameHandle cockpit = test.World.SpawnStaticFrame(fighter, TestWorld.At(1), radius: 2);
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                output.HandOffs.Add(new HandOff(fighterOwner, test.Root, cockpit, TestWorld.At(0), default, default));
        });

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
            IPhysicsBackend physics = TestPhysics.Create();
            Test = new TestWorld(physics);
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
            physics.OnStep((context, output) =>
            {
                if (context.Tick != 0)
                    return;
                output.Bodies.Add(new BodyState(Owner, Cockpit, TestWorld.At(-1), AnyVelocity, AnySpin));
                output.HandOffs.Add(new HandOff(Owner, Test.Root, Cockpit, TestWorld.At(-1), AnyVelocity, AnySpin));
                if (report == RefusedReport.BodyAndFrameState)
                    output.Frames.Add(new FrameState(Fighter, TestWorld.At(-1), new Double3(1, 2, 3), AnySpin, new Vector3(0, -2, 0)));
            });
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
        scene.World.GetPose(scene.Owner).Should().Be(TestWorld.At(100));
        scene.World.GetVelocity(scene.Owner).Should().Be(RefusedHandOffScene.OwnVelocity);
        scene.World.GetAngularVelocity(scene.Owner).Should().Be(RefusedHandOffScene.OwnSpin);
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
