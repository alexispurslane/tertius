using System.Collections.Concurrent;
using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The same inputs must give the same world, bit for bit, whether the gameplay chunks run on one thread or on eight.
/// This is the one place where the tests run the world in parallel. The workload sends messages in every delivery
/// mode, spawns, destroys, moves bodies between frames and reports contacts, so a change in the order of anything shows
/// in the hash. The four runs are made once, by <see cref="Runs"/>, and each test reads what it needs.
/// </summary>
public class DeterminismTests(DeterminismTests.Runs runs, ITestOutputHelper output) : IClassFixture<DeterminismTests.Runs>
{
    private const int Seed = 20261010;
    private const int Ticks = 200;

    private static readonly Topic Chat = Topics.Get("tests.determinism.chat");

    /// <summary>A seeded entity that moves, talks in every delivery mode, spawns, destroys and subscribes.</summary>
    private sealed class Chatter(ConcurrentDictionary<int, byte> threads) : IComponent
    {
        private static readonly EventMask WantsMask = EventMask.Of<Tick>().And<Ping>().And<Spawned>().And<FrameChanged>().And<Collision>();
        private readonly List<Entity> neighbors = new();

        public ulong Rng;
        public ulong Hash;
        public int Children;
        public int Received;
        public bool Subscribed;

        public int Priority => 0;
        public EventMask Wants => WantsMask;

        private double Next()
        {
            // xorshift64*: deterministic and seeded per entity.
            Rng ^= Rng >> 12;
            Rng ^= Rng << 25;
            Rng ^= Rng >> 27;
            return ((Rng * 0x2545F4914F6CDD1DUL) >> 11) * (1.0 / (1UL << 53));
        }

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            threads.TryAdd(Environment.CurrentManagedThreadId, 0);

            // Fold every event, in inbox order, into the hash: any change in routing order changes the result.
            for (int i = 0; i < events.Count; i++)
            {
                ref Event e = ref events[i];
                Hash = TestWorld.Mix(Hash, (ulong)e.Type);
                Hash = TestWorld.Mix(Hash, e.Sender.Raw);
                Hash = TestWorld.MixPayload(Hash, e);
                Received++;
            }
            if (!events.Contains<Tick>())
                return;

            if (self.Body.Exists)
                self.Body.AddForce(new Vector3((float)(Next() - 0.5), 0, (float)(Next() - 0.5)));
            else
            {
                ref DPose pose = ref self.Transform;
                pose = pose with { Position = self.Pose.Position + new Double3(Next() - 0.5, 0, Next() - 0.5) };
            }

            self.Snapshot.QueryRadius(self.Frame, self.Pose.Position, 30, neighbors);
            neighbors.Remove(self.Entity);
            int value = (int)(Rng & 0xFFFF);
            if (neighbors.Count > 0 && Next() < 0.3)
                outbox.Send(neighbors[(int)(Next() * neighbors.Count)], new Ping { Value = value });
            if (Next() < 0.03)
                outbox.Send(Chat, new Ping { Value = value + 1 });
            if (Next() < 0.03)
                outbox.Send(self.Frame.Topic, new Ping { Value = value + 2 });
            if (Next() < 0.003)
                outbox.Send(new Broadcast(), new Ping { Value = value + 3 });
            if (Next() < 0.05)
            {
                Target target = neighbors.Count > 0 ? neighbors[(int)(Next() * neighbors.Count)] : self.Entity;
                outbox.SendAfter(1 + (int)(Next() * 5), target, new Ping { Value = value + 4 });
            }
            if (Children < 2 && Next() < 0.012)
            {
                Children++;
                bool withBody = Next() < 0.3;
                Entity child = self.Spawn(new SpawnDesc
                {
                    Frame = self.Frame,
                    Pose = self.Pose,
                    Body = withBody ? TestWorld.Ball : null,
                    Components = Next() < 0.5
                        ? [new Chatter(threads) { Rng = Rng ^ 0x9E3779B97F4A7C15UL }, new Shield()]
                        : [new Chatter(threads) { Rng = Rng ^ 0xD1B54A32D192ED03UL }],
                    Subscriptions = Next() < 0.5 ? [Chat] : [],
                });
                outbox.Send(child, new Ping { Value = value + 5 });
            }
            if (neighbors.Count > 0 && Next() < 0.004)
                outbox.Send(neighbors[(int)(Next() * neighbors.Count)], new Destroy());
            if (Next() < 0.004)
                self.Destroy();
            if (Next() < 0.01)
            {
                if (Subscribed)
                    self.Unsubscribe(Chat);
                else
                    self.Subscribe(Chat);
                Subscribed = !Subscribed;
            }
        }
    }

    /// <summary>Refuses every other Destroy message.</summary>
    private sealed class Shield : IComponent
    {
        public int Refused;
        public int Priority => -1;
        public EventMask Wants => EventMask.Of<Destroy>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (++Refused % 2 == 1)
                events.RemoveAll(static (ref Destroy _) => true);
        }
    }

    /// <summary>What a run ended in. Two runs of the same inputs must give equal results.</summary>
    /// <param name="Hash">A hash of every live entity's handle, pose, velocities, frame and component state, the render snapshot, and the number of errors.</param>
    /// <param name="Stats">The counters of the last tick.</param>
    /// <param name="Messages">The sum, over the ticks, of the messages routed at each sync point.</param>
    /// <param name="HandOffs">The hand-offs that the physics double reported.</param>
    /// <param name="Contacts">The contacts that the physics double reported.</param>
    public sealed record RunResult(ulong Hash, WorldStats Stats, long Messages, int HandOffs, int Contacts);

    /// <summary>Runs the workload on <paramref name="maxDop"/> workers and returns what it ended in, and how many different threads ran gameplay.</summary>
    private static (RunResult Result, int GameplayThreads) Run(int maxDop, int seed = Seed, int ticks = Ticks)
    {
        int handOffs = 0, contacts = 0;
        var threads = new ConcurrentDictionary<int, byte>();
        var physics = new FakePhysics();
        // Debug checks are off: the invariant tests run them over a workload that churns more. Here they would only slow the four runs.
        var world = new World(new WorldOptions { MaxDegreeOfParallelism = maxDop, ChunkSize = 16, SpawnPoolSize = 16, DebugChecks = false }, physics);
        // The Kepler frame below takes its G·M from the point gravity of the root.
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e12f), MotionDriver.Static, GravityField.Point(1e9)));
        FrameHandle west = world.SpawnStaticFrame(root, TestWorld.At(-1000), radius: 200);
        FrameHandle east = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Kepler, Orbit: KeplerOrbit.Circular(1000, spinRate: 0.05)),
        }));

        // Bodies that drift past x = 20 in the west frame are handed off to the east frame, and close bodies collide.
        physics.OnStep = (context, output) =>
        {
            Entity previous = default;
            DPose previousPose = default;
            foreach ((Entity entity, FakePhysics.Body body) in physics.Bodies)
            {
                if (body.Frame == west && body.Pose.Position.X > 20)
                {
                    output.HandOffs.Add(new HandOff(entity, west, east, body.Pose with { Position = body.Pose.Position - new Double3(40, 0, 0) }, body.Velocity, default));
                    handOffs++;
                }
                if (!previous.IsNull && (previousPose.Position - body.Pose.Position).Length < 1.5)
                {
                    output.Contacts.Add(new ContactStarted(previous, entity, body.Frame, body.Pose.Position, Vector3.UnitY, 1));
                    contacts++;
                }
                previous = entity;
                previousPose = body.Pose;
            }
        };

        var random = new Random(seed);
        for (int i = 0; i < 160; i++)
        {
            FrameHandle frame = (i % 3) switch { 0 => west, 1 => east, _ => root };
            world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = TestWorld.At(random.NextDouble() * 60 - 30, 0, random.NextDouble() * 60 - 30),
                Body = i % 4 == 0 ? TestWorld.Ball : null,
                Render = new RenderData { Mesh = new MeshId(i % 3), Flags = RenderFlags.Visible },
                LinearVelocity = new Vector3((float)random.NextDouble(), 0, 0),
                Components = i % 5 == 0
                    ? [new Chatter(threads) { Rng = (ulong)random.NextInt64() | 1 }, new Shield()]
                    : [new Chatter(threads) { Rng = (ulong)random.NextInt64() | 1 }],
                Subscriptions = i % 2 == 0 ? [Chat] : [],
            });
        }
        world.Camera = world.Spawn(new SpawnDesc { Frame = west, Render = new RenderData() });

        long messages = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            messages += world.Stats.RoutedEvents;
        }

        ulong hash = 0;
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        foreach (Entity e in live)
        {
            hash = TestWorld.Mix(hash, e.Raw);
            hash = TestWorld.Mix(hash, world.GetPose(e));
            hash = TestWorld.Mix(hash, world.GetVelocity(e));
            hash = TestWorld.Mix(hash, world.GetAngularVelocity(e));
            hash = TestWorld.Mix(hash, (ulong)world.GetFrame(e).Index);
            foreach (IComponent component in world.GetComponents(e))
            {
                switch (component)
                {
                    case Chatter c:
                        hash = TestWorld.Mix(TestWorld.Mix(TestWorld.Mix(hash, c.Hash), c.Rng), (ulong)(c.Children * 1000 + c.Received));
                        break;
                    case Shield s:
                        hash = TestWorld.Mix(hash, (ulong)s.Refused);
                        break;
                }
            }
        }
        world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot).Should().BeTrue("the sync point publishes a render snapshot every tick");
        foreach (RenderInstance instance in snapshot.Instances)
            hash = TestWorld.Mix(TestWorld.Mix(TestWorld.Mix(hash, instance.EntityIndex), instance.Position), instance.PreviousPosition);
        hash = TestWorld.Mix(hash, (ulong)world.Errors.Count);
        WorldStats stats = world.Stats;
        world.Dispose();
        return (new RunResult(hash, stats, messages, handOffs, contacts), threads.Count);
    }

    /// <summary>The four runs, made once for the whole class: two on one worker and two on eight, alternating.</summary>
    public sealed class Runs
    {
        public Runs()
        {
            (OneWorker, _) = Run(1);
            (EightWorkers, EightWorkersGameplayThreads) = Run(8);
            (OneWorkerAgain, _) = Run(1);
            (EightWorkersAgain, _) = Run(8);
        }

        public RunResult OneWorker { get; }
        public RunResult EightWorkers { get; }
        public RunResult OneWorkerAgain { get; }
        public RunResult EightWorkersAgain { get; }

        /// <summary>How many different threads ran gameplay in the first run on eight workers.</summary>
        public int EightWorkersGameplayThreads { get; }
    }

    [Fact]
    public void TwoRunsOnOneWorkerEndInTheSameWorld()
    {
        output.WriteLine(runs.OneWorker.ToString());

        runs.OneWorkerAgain.Hash.Should().Be(runs.OneWorker.Hash);
        runs.OneWorkerAgain.Should().Be(runs.OneWorker);
    }

    [Fact]
    public void ARunOnEightWorkersEndsInTheWorldOfARunOnOne()
    {
        runs.EightWorkers.Hash.Should().Be(runs.OneWorker.Hash);
        runs.EightWorkers.Stats.Should().Be(runs.OneWorker.Stats);
        runs.EightWorkers.Should().Be(runs.OneWorker);
    }

    [Fact]
    public void TwoRunsOnEightWorkersEndInTheSameWorld()
    {
        runs.EightWorkersAgain.Hash.Should().Be(runs.EightWorkers.Hash);
        runs.EightWorkersAgain.Should().Be(runs.EightWorkers);
    }

    [Fact]
    public void TheRunOnEightWorkersReallyRunsGameplayOnSeveralThreads()
    {
        // Equal hashes would prove nothing if every chunk ran on the one thread that called Tick.
        if (Environment.ProcessorCount == 1)
            return;

        runs.EightWorkersGameplayThreads.Should().BeGreaterThan(1);
    }

    [Fact]
    public void ADifferentSeedEndsInADifferentWorld()
    {
        // The hash must be able to tell worlds apart, or its equality above proves nothing.
        (RunResult other, _) = Run(1, seed: Seed + 1, ticks: 60);
        (RunResult same, _) = Run(1, seed: Seed, ticks: 60);

        other.Hash.Should().NotBe(same.Hash);
    }

    [Fact]
    public void TheWorkloadKeepsManyEntitiesAliveAndMovesManyMessages()
    {
        runs.OneWorker.Stats.LiveEntities.Should().BeGreaterThan(150);
        runs.OneWorker.Messages.Should().BeGreaterThan(10_000);
    }

    [Fact]
    public void TheWorkloadHandsBodiesOffBetweenFramesAndReportsContacts()
    {
        runs.OneWorker.HandOffs.Should().BeGreaterThan(0);
        runs.OneWorker.Contacts.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NoSpawnTakesItsSlotFromTheGlobalFallbackBecauseThatMakesSlotsDependOnThreadTiming()
    {
        // With the pool large enough, every spawn gets a slot from its own chunk's pool, so its handle is the same in every run.
        runs.OneWorker.Stats.FallbackSpawns.Should().Be(0);
        runs.EightWorkers.Stats.FallbackSpawns.Should().Be(0);
    }
}
