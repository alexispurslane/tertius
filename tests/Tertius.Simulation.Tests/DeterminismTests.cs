using System.Numerics;

namespace Tertius.Simulation.Tests;

public class DeterminismTests(ITestOutputHelper output)
{
    private static readonly Topic Chat = Topics.Get("tests.determinism.chat");

    /// <summary>A seeded entity that moves, talks in every delivery mode, spawns, destroys and subscribes.</summary>
    private sealed class Chatter : IComponent
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
                        ? [new Chatter { Rng = Rng ^ 0x9E3779B97F4A7C15UL }, new Shield()]
                        : [new Chatter { Rng = Rng ^ 0xD1B54A32D192ED03UL }],
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

    private sealed record RunResult(ulong Hash, WorldStats Stats, long Messages, int HandOffs, int Contacts);

    private static RunResult Run(int maxDop)
    {
        int handOffs = 0, contacts = 0;
        var physics = new FakePhysics();
        var world = new World(new WorldOptions { MaxDegreeOfParallelism = maxDop, ChunkSize = 16, SpawnPoolSize = 16, DebugChecks = true }, physics);
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e12f), MotionDriver.Static));
        FrameHandle west = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(-1000), AsFrame = new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Static) }));
        FrameHandle east = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(200), MotionDriver.Kepler, Orbit: KeplerOrbit.Circular(1000, 1e9, spinRate: 0.05)),
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

        var seed = new Random(20261010);
        for (int i = 0; i < 240; i++)
        {
            FrameHandle frame = (i % 3) switch { 0 => west, 1 => east, _ => root };
            world.Spawn(new SpawnDesc
            {
                Frame = frame,
                Pose = TestWorld.At(seed.NextDouble() * 60 - 30, 0, seed.NextDouble() * 60 - 30),
                Body = i % 4 == 0 ? TestWorld.Ball : null,
                Render = new RenderData { Mesh = new MeshId(i % 3), Flags = RenderFlags.Visible },
                LinearVelocity = new Vector3((float)seed.NextDouble(), 0, 0),
                Components = i % 5 == 0
                    ? [new Chatter { Rng = (ulong)seed.NextInt64() | 1 }, new Shield()]
                    : [new Chatter { Rng = (ulong)seed.NextInt64() | 1 }],
                Subscriptions = i % 2 == 0 ? [Chat] : [],
            });
        }
        var camera = world.Spawn(new SpawnDesc { Frame = west, Render = new RenderData() });
        world.Camera = camera;

        long messages = 0;
        for (int tick = 0; tick < 300; tick++)
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
        Assert.True(world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot));
        foreach (RenderInstance instance in snapshot.Instances)
            hash = TestWorld.Mix(TestWorld.Mix(TestWorld.Mix(hash, instance.EntityIndex), instance.Position), instance.PreviousPosition);
        hash = TestWorld.Mix(hash, (ulong)world.Errors.Count);
        WorldStats stats = world.Stats;
        world.Dispose();
        return new RunResult(hash, stats, messages, handOffs, contacts);
    }

    [Fact]
    public void TwoRunsAreBitIdenticalWhateverTheParallelism()
    {
        RunResult serial = Run(1);
        RunResult parallel = Run(8);
        RunResult serialAgain = Run(1);
        RunResult parallelAgain = Run(8);

        output.WriteLine(serial.ToString());

        // The workload is real: entities came and went, and many messages flowed.
        Assert.Equal(0, serial.Stats.FallbackSpawns);
        Assert.True(serial.Stats.LiveEntities > 150, $"live: {serial.Stats.LiveEntities}");
        Assert.True(serial.Messages > 10_000, $"messages: {serial.Messages}");
        Assert.True(serial.HandOffs > 0 && serial.Contacts > 0, serial.ToString());

        Assert.Equal(serial.Hash, parallel.Hash);
        Assert.Equal(serial.Hash, serialAgain.Hash);
        Assert.Equal(serial.Hash, parallelAgain.Hash);
        Assert.Equal(serial.Stats, parallel.Stats);
        Assert.Equal(serial, parallel);
        Assert.Equal(serial, serialAgain);
        Assert.Equal(serial, parallelAgain);
    }
}
