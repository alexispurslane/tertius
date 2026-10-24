using System.Numerics;
using System.Reflection;
using JoltPhysicsSharp;
using Tertius.Simulation;
using static Tertius.Testing.ContactNews;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The backend hears Jolt's contacts through its own joltc contact listener, whose callbacks are C# functions that Jolt
/// calls directly, and not through the contact events of JoltPhysicsSharp (ter-pnco). joltc keeps one table of contact
/// callbacks for the whole process, so the backend's table replaces the binding's. The binding's events can then never
/// fire, so no code may subscribe to them, and every system must use the backend's listener, also a system that a growth
/// made.
/// </summary>
public sealed class ContactListenerTests
{
    /// <summary>A ball of radius 0.5 m that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc BallDesc = new(
        Shape: new SphereShapeDesc(0.5f),
        Mass: 1,
        Restitution: 0,
        LinearDamping: 0,
        AngularDamping: 0
    );

    /// <summary>A static cube with sides of 1 m that does not bounce. Jolt takes the larger restitution of two bodies.</summary>
    private static readonly BodyDesc WallDesc = new(
        Shape: new BoxShapeDesc(new(0.5f)),
        Motion: BodyMotion.Static,
        Restitution: 0
    );

    /// <summary>The backing fields of the binding's four contact events on <see cref="PhysicsSystem"/>.</summary>
    private static readonly FieldInfo[] BindingContactEvents =
    [
        .. new[]
        {
            nameof(PhysicsSystem.OnContactValidate),
            nameof(PhysicsSystem.OnContactAdded),
            nameof(PhysicsSystem.OnContactPersisted),
            nameof(PhysicsSystem.OnContactRemoved),
        }.Select(name =>
            typeof(PhysicsSystem).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"JoltPhysicsSharp has no backing field for {name}, so the test cannot see its handlers."
            )
        ),
    ];

    /// <summary>
    /// A frame with no gravity, whose system holds 8 bodies at first. The near ball rests 1 cm from the near wall, inside
    /// Jolt's speculative contact distance of 2 cm, so Jolt finds their pair without a touch and the pair waits for its
    /// first touch. The far ball rests 20 cm from the far wall, where Jolt finds nothing.
    /// </summary>
    private sealed class Walls : IDisposable
    {
        public Walls()
        {
            World = WorldTestingGrowth.NewWorldWithFaultsAndSmallSystems(
                maxBodiesPerFrame: 8,
                faults: out FaultInjectingBackend faults,
                warnings: Warnings
            );
            Backend = faults.Jolt;
            Space = Ground(World, 0);
            NearWall = World.Spawn(
                new()
                {
                    Frame = Space,
                    Pose = At(1.01),
                    Body = WallDesc,
                }
            );
            Near = World.Spawn(
                new()
                {
                    Frame = Space,
                    Body = BallDesc,
                    Components = [NearLog, NearPilot],
                }
            );
            FarWall = World.Spawn(
                new()
                {
                    Frame = Space,
                    Pose = At(1.2, 0, 10),
                    Body = WallDesc,
                }
            );
            Far = World.Spawn(
                new()
                {
                    Frame = Space,
                    Pose = At(0, 0, 10),
                    Body = BallDesc,
                    Components = [FarLog, FarPilot],
                }
            );
            Tick(World, 10);
        }

        public List<string> Warnings { get; } = [];
        public World World { get; }
        public JoltPhysicsBackend Backend { get; }
        public FrameHandle Space { get; }
        public Entity NearWall { get; }
        public Entity Near { get; }
        public CollisionLog NearLog { get; } = new();
        public Pilot NearPilot { get; } = new();
        public Entity FarWall { get; }
        public Entity Far { get; }
        public CollisionLog FarLog { get; } = new();
        public Pilot FarPilot { get; } = new();

        /// <summary>The number of times that a system grew so far.</summary>
        public int Growths => Warnings.Count(warning => warning.Contains("so it grew"));

        /// <summary>Adds 15 static crates that touch nothing, which grow the system from 8 bodies to 16 and then to 32, and ticks 10 times.</summary>
        public void Grow()
        {
            for (int i = 0; i < 15; i++)
                World.Spawn(
                    new()
                    {
                        Frame = Space,
                        Pose = At(3 * i, -100),
                        Body = WallDesc,
                    }
                );
            Tick(World, 10);
        }

        public void Dispose() => World.Dispose();
    }

    [Fact]
    public void ContactsWaitStartAndEndInASystemThatGrew()
    {
        using var scene = new Walls();
        scene.Backend.PendingPairCount(scene.Space).Should().Be(1, "Jolt found the near ball and its wall 1 cm apart");

        scene.Grow();
        scene.Growths.Should().Be(2);
        scene
            .Backend.PendingPairCount(scene.Space)
            .Should()
            .Be(1, "the grown system found the pair again, still without a touch");

        // The near ball closes its gap of 1 cm in two steps, which only Jolt's report of a persisting contact shows. The
        // far ball hits its wall at 2 m/s, which Jolt reports as a new contact.
        scene.NearPilot.Once = body => body.SetVelocity(new(0.5, 0, 0));
        scene.FarPilot.Once = body => body.SetVelocity(new(2, 0, 0));
        Tick(scene.World, 10);
        scene.NearLog.NewsOf(scene.NearWall).Should().Equal(Started);
        scene.FarLog.NewsOf(scene.FarWall).Should().Equal(Started);
        scene.Backend.PendingPairCount(scene.Space).Should().Be(0);

        // The near ball leaves its wall while it is awake, which Jolt reports as a removed contact.
        scene.NearPilot.Once = body => body.SetVelocity(new(-1, 0, 0));
        Tick(scene.World, 10);
        scene.NearLog.NewsOf(scene.NearWall).Should().Equal(Started, Ended);
        scene.FarLog.NewsOf(scene.FarWall).Should().Equal([Started], "the far ball still rests against its wall");
        scene.World.Errors.Should().BeEmpty();
    }

    private static void ShouldHaveNoBindingHandlers(PhysicsSystem? system, string when)
    {
        system.Should().NotBeNull();
        foreach (FieldInfo field in BindingContactEvents)
            field
                .GetValue(system)
                .Should()
                .BeNull($"no code subscribes to {field.Name} of JoltPhysicsSharp, which never fires ({when})");
    }

    [Fact]
    public void NoCodeSubscribesToTheContactEventsOfJoltPhysicsSharp()
    {
        int stray = ContactListener.StrayReports;
        using var scene = new Walls();
        ShouldHaveNoBindingHandlers(scene.Backend.SystemOf(scene.Space), "with a pending pair");

        scene.Grow();
        ShouldHaveNoBindingHandlers(scene.Backend.SystemOf(scene.Space), "after two growths");

        scene.NearPilot.Once = body => body.SetVelocity(new(0.5, 0, 0));
        Tick(scene.World, 10);
        scene.NearPilot.Once = body => body.SetVelocity(new(-1, 0, 0));
        Tick(scene.World, 10);
        ShouldHaveNoBindingHandlers(scene.Backend.SystemOf(scene.Space), "after a contact started and ended");
        scene.NearLog.NewsOf(scene.NearWall).Should().Equal([Started, Ended], "the backend did hear the contacts");
        ContactListener
            .StrayReports.Should()
            .Be(stray, "every system of the backend, also each one that a growth made, uses the backend's listener");
        scene.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A system that the backend did not make keeps the listener that JoltPhysicsSharp gave it, and that listener now calls
    /// the backend's callbacks, with JoltPhysicsSharp's own user data. The callbacks see that it names no frame, count each
    /// report and leave it, instead of reading JoltPhysicsSharp's object as a frame.
    /// </summary>
    [Fact]
    public void TheCallbacksCountAndLeaveTheReportsOfASystemThatKeptTheListenerOfJoltPhysicsSharp()
    {
        // The backend makes joltc call its callbacks for every listener in the process.
        using var backend = new JoltPhysicsBackend(new() { WorkerThreads = 1 });
        int stray = ContactListener.StrayReports;
        // The system steps on this thread with no lock: joltc gives each calling thread its own temp allocator.
        var layers = new Layers.Tables();
        using PhysicsSystem system = NativeOwnership.Owned(
            new PhysicsSystem(
                new()
                {
                    MaxBodies = 16,
                    MaxBodyPairs = 16,
                    MaxContactConstraints = 16,
                    ObjectLayerPairFilter = layers.Pairs,
                    BroadPhaseLayerInterface = layers.BroadPhase,
                    ObjectVsBroadPhaseLayerFilter = layers.ObjectVsBroadPhase,
                }
            )
        );
        using JobSystemThreadPool jobs = NativeOwnership.Owned(new JobSystemThreadPool(new() { numThreads = 1 }));
        using BoxShape cube = NativeOwnership.Owned(new BoxShape(new Vector3(0.5f)));
        ObjectLayer moving = Layers.For(
            BodyLayer.Moving,
            CollisionGroups.Of(CollisionGroup.Solid),
            CollisionMask.Empty.InAny(CollisionGroup.Solid)
        );
        // Two boxes that overlap by 10 cm, so that Jolt reports their contact in every update.
        foreach (double x in new[] { 0.0, 0.9 })
        {
            using BodyCreationSettings settings = NativeOwnership.Owned(
                new BodyCreationSettings(
                    shape: cube,
                    position: new RVector3(x, 0, 0),
                    rotation: Quaternion.Identity,
                    motionType: MotionType.Dynamic,
                    objectLayer: moving
                )
            );
            system.BodyInterface.CreateAndAddBody(settings, Activation.Activate);
        }

        for (int i = 0; i < 3; i++)
            system.Update(1 / 60f, 1, jobs);

        ContactListener
            .StrayReports.Should()
            .BeGreaterThan(
                stray,
                "Jolt reported the contact of the two boxes through the listener of JoltPhysicsSharp"
            );
    }
}

/// <summary>Runs the tests of <see cref="ContactAllocationTests"/> alone, because they count the bytes that every thread allocates.</summary>
[CollectionDefinition(nameof(ContactAllocationTests), DisableParallelization = true)]
public sealed class ContactAllocationCollection;

/// <summary>
/// Jolt reports every contact of every awake body in every step, on its worker threads, so the backend's callbacks for
/// those reports must not allocate. The test counts the bytes that the whole process allocates, because the callbacks run
/// on Jolt's threads, so it runs while no other test runs.
/// </summary>
[Collection(nameof(ContactAllocationTests))]
public sealed class ContactAllocationTests
{
    private const int Columns = 10;
    private const int Rows = 10;

    /// <summary>
    /// 100 boxes of 1 m ride a kinematic floor that moves at 1 m/s, so every box stays awake. They stand in 10 rows of
    /// 10, each box against its neighbors in the row and the rows 5 cm apart, so Jolt reports 190 contacts again in every
    /// step: 100 with the floor and 90 between neighbors. The first box of the first row stands 1 cm from its neighbor,
    /// so that pair waits for its first touch, and the backend must look at every report.
    /// </summary>
    [Fact]
    public void StepsWithManyPersistingContactsAndAPendingPairAllocateNothingOnAnyThread()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend, debugChecks: false);
        FrameHandle ground = Ground(world, 9.81f);
        world.Spawn(
            KinematicDrift.Drifting(
                new()
                {
                    Frame = ground,
                    Pose = At(0, -0.5),
                    Body = new(Shape: new BoxShapeDesc(new(60, 0.5f, 60)), Motion: BodyMotion.Kinematic),
                },
                world,
                new(1, 0, 0)
            )
        );
        for (int i = 0; i < Columns * Rows; i++)
        {
            double x = i % Columns - Columns / 2 - (i == 0 ? 0.01 : 0);
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = At(x, 0.5, (i / Columns - Rows / 2) * 1.05),
                    Body = Cube(),
                }
            );
        }

        // Two seconds: every box has the speed of the floor.
        Tick(world, 120);
        backend.PendingPairCount(ground).Should().Be(1);
        backend
            .BodiesIn(ground)
            .Count(body => body.Active && body.Motion == MotionType.Dynamic)
            .Should()
            .Be(Columns * Rows, "every box rides the floor");

        // The test counts the bytes per report, not in all. An allocation in a callback happens at every report, so it gives at
        // least 1 byte per report. The runtime can compile a hot method again during one window, which allocates once, so the
        // test takes the smaller window. It collects no garbage: the callbacks hold no managed objects that a collection could
        // drop, while the backend's gravity pass looks up JoltPhysicsSharp's wrapper of each awake body, which a collection
        // drops and the next step makes again.
        const int Ticks = 200;
        const int ReportsPerTick = Columns * Rows + Rows * (Columns - 1);
        long first = BytesPerReport(world, Ticks, ReportsPerTick);
        long second = BytesPerReport(world, Ticks, ReportsPerTick);

        Math.Min(first, second)
            .Should()
            .Be(
                0,
                $"a report of a persisting contact must not allocate (the windows gave {first} and {second} bytes per report)"
            );
        backend
            .PendingPairCount(ground)
            .Should()
            .Be(1, "the pair waited the whole time, so every report went through the pending lookup");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// With no gravity, a kinematic block in a frame of radius 10 m sticks 1 m out of the bubble, so space holds a ghost of
    /// it, whose face is at x = 11 m. 16 balls of radius 0.1 m in space, in a grid of lanes 0.4 m apart, bounce at 3 m/s
    /// between that face and the face of a static wall at x = 13.5 m, with a restitution of 1. So every 1.5 s each ball
    /// starts and ends a contact with the ghost, which goes through the backend's count of the systems that hold each pair
    /// of entities, and one with the wall. Once each pair has been counted, a step must allocate nothing.
    /// </summary>
    [Fact]
    public void StepsWithContactsThroughAGhostThatStartAndEndAllocateNothingOnAnyThread()
    {
        using World world = NewWorld(backend: out _, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle bubble = world.GetOwnedFrame(
            world.Spawn(new() { Frame = space, AsFrame = new(FrameBounds.Sphere(10)) })
        );
        var blockContacts = new ContactCounter();
        world.Spawn(
            new()
            {
                Frame = bubble,
                Pose = At(10),
                Body = new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Kinematic),
                Components = [blockContacts],
            }
        );
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(13.75),
                Body = new(Shape: new BoxShapeDesc(new(0.25f, 1, 1)), Motion: BodyMotion.Static),
            }
        );
        var bouncy = new BodyDesc(
            Shape: new SphereShapeDesc(0.1f),
            Mass: 1,
            Restitution: 1,
            LinearDamping: 0,
            AngularDamping: 0
        );
        for (int i = 0; i < 16; i++)
            world.Spawn(
                new()
                {
                    Frame = space,
                    Pose = At(12, -0.6 + 0.4 * (i % 4), -0.6 + 0.4 * (i / 4)),
                    LinearVelocity = new Vector3(3, 0, 0),
                    Body = bouncy,
                }
            );
        // Two seconds: each ball has hit the ghost once.
        Tick(world, 120);
        blockContacts.Started.Should().Be(16, "each ball hit the block's ghost");

        const int Ticks = 200;
        int startedBefore = blockContacts.Started;
        long first = BytesPerTick(world, Ticks);
        long second = BytesPerTick(world, Ticks);

        Math.Min(first, second)
            .Should()
            .Be(
                0,
                $"a step with contacts through a ghost must not allocate (the windows gave {first} and {second} bytes per tick)"
            );
        (blockContacts.Started - startedBefore)
            .Should()
            .BeGreaterThanOrEqualTo(32, "each ball hit the ghost again in the windows");
        blockContacts.Ended.Should().BeGreaterThanOrEqualTo(blockContacts.Started - 16);
        world.Errors.Should().BeEmpty();
    }

    private static long BytesPerTick(World world, int ticks)
    {
        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (int t = 0; t < ticks; t++)
            world.Tick();
        return (GC.GetTotalAllocatedBytes(precise: true) - before) / ticks;
    }

    /// <summary>Counts the Collision and CollisionEnded events that its entity hears, without allocating.</summary>
    private sealed class ContactCounter : IComponent
    {
        public int Started;
        public int Ended;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Collision>() | EventMask.Of<CollisionEnded>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].Is<Collision>())
                    Started++;
                else if (events[i].Is<CollisionEnded>())
                    Ended++;
        }
    }

    private static long BytesPerReport(World world, int ticks, int reportsPerTick)
    {
        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (int t = 0; t < ticks; t++)
            world.Tick();
        return (GC.GetTotalAllocatedBytes(precise: true) - before) / ((long)ticks * reportsPerTick);
    }
}
