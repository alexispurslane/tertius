using System.Numerics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The world's invariant checks over a workload that churns every subsystem at once, and the debug switch that runs
/// them at every sync point.
/// </summary>
public class InvariantTests(ITestOutputHelper output)
{
    private static readonly Topic Chatter = Topics.Get("tests.invariants.chatter");

    /// <summary>What the churn did, so that the test can show that the workload reached every subsystem.</summary>
    private sealed class Tally
    {
        public int Spawns, AttachedSpawns, FrameSpawns, Destroys, Subscriptions, ComponentChanges, Moves;
    }

    /// <summary>
    /// Does one random thing per tick from a seeded generator: spawn (plain, attached, with a body, or a frame), destroy
    /// itself or a neighbor, subscribe or unsubscribe, send, change its components, or move.
    /// </summary>
    private sealed class Churn(ulong seed, Entity rootOwner, Tally tally) : IComponent
    {
        private readonly List<Entity> neighbors = new();
        private ulong rng = seed | 1;
        private Recorder? extra;

        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>().And<Ping>().And<FrameChanged>().And<Collision>();

        private int Next(int n)
        {
            rng ^= rng << 13;
            rng ^= rng >> 7;
            rng ^= rng << 17;
            return (int)(rng % (ulong)n);
        }

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (!events.Contains<Tick>() || self.Frame.IsNull)
                return;
            DPose here = new(self.Pose.Position + new Double3(Next(21) - 10, 0, Next(21) - 10), DQuaternion.Identity);
            int roll = Next(100);
            if (roll < 3)
            {
                self.Spawn(new SpawnDesc { Frame = self.Frame, Pose = here, Components = [new Churn(rng + 1, rootOwner, tally)] });
                tally.Spawns++;
            }
            else if (roll < 5 && !self.Body.Exists)
            {
                self.Spawn(new SpawnDesc { Frame = self.Frame, AttachTo = self.Entity, AttachOffset = new Pose(new Vector3(1, 0, 0), Quaternion.Identity) });
                tally.AttachedSpawns++;
            }
            else if (roll < 7)
            {
                self.Spawn(new SpawnDesc { Frame = self.Frame, Pose = here, Body = TestWorld.Ball, LinearVelocity = new Vector3(Next(3) - 1, 0, 0), Components = [new Churn(rng + 2, rootOwner, tally)] });
                tally.Spawns++;
            }
            else if (roll < 8)
            {
                self.Spawn(new SpawnDesc
                {
                    Frame = self.Frame,
                    Pose = here,
                    AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Static),
                    Components = [new Churn(rng + 3, rootOwner, tally)],
                    Subscriptions = [Chatter],
                });
                tally.FrameSpawns++;
            }
            else if (roll < 13)
            {
                self.Destroy();
                tally.Destroys++;
            }
            else if (roll < 17)
            {
                self.Snapshot.QueryRadius(self.Frame, self.Pose.Position, 40, neighbors);
                neighbors.Remove(rootOwner);
                if (neighbors.Count > 0)
                {
                    outbox.Send(neighbors[Next(neighbors.Count)], new Destroy());
                    tally.Destroys++;
                }
            }
            else if (roll < 24)
            {
                Topic topic = Next(2) == 0 ? Chatter : self.Frame.Topic;
                if (Next(2) == 0)
                    self.Subscribe(topic);
                else
                    self.Unsubscribe(topic);
                tally.Subscriptions++;
            }
            else if (roll < 30)
            {
                outbox.Send(Chatter, new Ping { Value = roll });
                outbox.Send(self.Frame.Topic, new Ping { Value = roll });
                outbox.SendAfter(1 + Next(3), new Broadcast(), new Ping { Value = roll });
            }
            else if (roll < 33)
            {
                if (extra == null)
                {
                    extra = new Recorder(EventMask.Of<Heal>(), priority: Next(3) - 1);
                    self.AddComponent(extra);
                }
                else
                {
                    self.RemoveComponent(extra);
                    extra = null;
                }
                tally.ComponentChanges++;
            }
            else if (roll < 50 && !self.Body.Exists && self.AttachedTo.IsNull)
            {
                self.Transform = here;
                self.Render.Scale = new Vector3(1 + Next(3));
                tally.Moves++;
            }
            else if (self.Body.Exists)
            {
                self.Body.AddForce(new Vector3(Next(3) - 1, 0, 0));
            }
        }
    }

    [Fact]
    public void TheInvariantsHoldThroughAWorkloadThatChurnsEverySubsystem()
    {
        var tally = new Tally();
        int handOffs = 0, frameStates = 0;
        var physics = new FakePhysics();
        using World world = TestWorld.Create(out FrameHandle root, physics: physics, chunkSize: 8);
        Entity rootOwner = root.Entity;
        Entity shuttleOwner = world.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = TestWorld.At(50),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
            Body = new BodyDesc(new SphereShapeDesc(5), Layer: BodyLayer.FrameHull),
            Components = [new Churn(99, rootOwner, tally)],
        });
        FrameHandle shuttle = world.GetOwnedFrame(shuttleOwner);
        world.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = TestWorld.At(-200),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Kepler, Orbit: KeplerOrbit.Circular(200, 1e9, spinRate: 0.1)),
        });

        // Every few steps physics moves a body into another live frame, and the shuttle reports a new state.
        var frames = new List<FrameHandle>();
        physics.OnStep = (context, output) =>
        {
            if (context.Frames.IsAlive(shuttle))
            {
                output.Frames.Add(new FrameState(shuttle, TestWorld.At(50 + context.Tick * 0.1), new Double3(6, 0, 0), Vector3.Zero, Vector3.Zero));
                frameStates++;
            }
            if (context.Tick % 3 != 0)
                return;
            frames.Clear();
            context.Frames.GetAll(frames);
            foreach ((Entity entity, FakePhysics.Body body) in physics.Bodies)
            {
                FrameHandle to = frames[(int)((ulong)context.Tick * 2654435761UL % (ulong)frames.Count)];
                bool cycle = entity == shuttleOwner && (to == shuttle || context.Frames.IsAncestorOf(shuttle, to));
                if (to == body.Frame || cycle)
                    continue;
                output.HandOffs.Add(new HandOff(entity, body.Frame, to, body.Pose, body.Velocity, default));
                handOffs++;
                break;
            }
        };

        var random = new Random(20261010);
        for (int i = 0; i < 40; i++)
            world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(random.Next(-100, 100), 0, random.Next(-100, 100)), Components = [new Churn((ulong)random.NextInt64(), rootOwner, tally)] });
        world.AssertInvariants();

        var live = new List<Entity>();
        for (int tick = 0; tick < 150; tick++)
        {
            // DebugChecks run the checks inside every sync point; the outside-tick changes are checked here.
            world.Tick();
            live.Clear();
            world.CopyLiveEntities(live);
            live.Remove(rootOwner);
            if (tick % 10 == 0 && live.Count > 0)
            {
                world.Destroy(live[random.Next(live.Count)]);
                world.AssertInvariants();
            }
            if (tick % 5 == 0)
            {
                for (int i = 0; i < 4; i++)
                    world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(random.Next(-100, 100)), Components = [new Churn((ulong)random.NextInt64(), rootOwner, tally)] });
                world.AssertInvariants();
            }
            if (tick % 7 == 0 && live.Count > 0 && world.IsAlive(live[0]))
            {
                world.Subscribe(live[0], Chatter);
                world.AddComponent(live[0], new Recorder(EventMask.Of<Heal>()));
                world.AssertInvariants();
            }
        }

        output.WriteLine($"{world.Stats}; spawns {tally.Spawns}, attached {tally.AttachedSpawns}, frames {tally.FrameSpawns}, destroys {tally.Destroys}, " +
                         $"subscriptions {tally.Subscriptions}, component changes {tally.ComponentChanges}, moves {tally.Moves}, hand-offs {handOffs}, frame states {frameStates}, errors {world.Errors.Count}");
        Assert.True(world.Stats.LiveEntities > 20, world.Stats.ToString());
        Assert.True(tally.Spawns > 50 && tally.AttachedSpawns > 10 && tally.FrameSpawns > 5, $"{tally.Spawns} {tally.AttachedSpawns} {tally.FrameSpawns}");
        Assert.True(tally.Destroys > 50 && tally.Subscriptions > 50 && tally.ComponentChanges > 20 && tally.Moves > 100);
        Assert.True(handOffs > 10 && frameStates > 0, $"{handOffs} {frameStates}");
        Assert.DoesNotContain(world.Errors, e => e.Contains("Exception"));
    }

    [Fact]
    public void DebugChecksCatchABrokenInvariantAtTheNextSyncPoint()
    {
        // Destroying a frame behind the world's back leaves its owner and its contents pointing at a dead frame.
        static World Broken(bool debugChecks)
        {
            var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = debugChecks });
            FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static));
            FrameHandle bay = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static) }));
            world.SpawnAt(bay, new Double3(1, 0, 0));
            world.Tick();
            world.Frames.Destroy(bay);
            return world;
        }

        using (World checkedWorld = Broken(debugChecks: true))
        {
            Assert.Throws<ContractException>(checkedWorld.AssertInvariants);
            Assert.Throws<ContractException>(checkedWorld.Tick);
        }
        using (World uncheckedWorld = Broken(debugChecks: false))
            uncheckedWorld.Tick();
    }
}
