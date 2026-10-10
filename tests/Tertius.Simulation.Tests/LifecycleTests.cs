using System.Numerics;

namespace Tertius.Simulation.Tests;

public class LifecycleTests
{
    private sealed class Shield : IComponent
    {
        public int Priority => -10;
        public EventMask Wants => EventMask.Of<Destroy>();
        public int Blocked;

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            Blocked += events.RemoveAll(static (ref Destroy _) => true);
    }

    [Fact]
    public void ASpawnDuringATickReturnsAFinalHandleAndRunsOnTheNextTick()
    {
        using World world = TestWorld.Create(out FrameHandle root, maxDop: 4);
        var childLog = new Recorder(EventMask.Of<Tick>().And<Spawned>().And<Ping>());
        Entity child = default;
        bool liveDuringTick = true;
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick != 0)
                return;
            child = self.Spawn(new SpawnDesc { Frame = self.Frame, Pose = TestWorld.At(3), Components = [childLog] });
            liveDuringTick = self.Snapshot.IsAlive(child);
            outbox.Send(child, new Ping { Value = 5 });
        }));

        world.Tick();
        Assert.False(liveDuringTick);
        Assert.True(world.IsAlive(child));
        Assert.Equal(3, world.GetPose(child).Position.X);
        Assert.Equal(root, world.GetFrame(child));
        Assert.Empty(childLog.Seen);

        world.Tick();
        Assert.Equal(3, childLog.Seen.Count);
        Assert.True(childLog.Seen[0].Event.Is<Tick>());
        Assert.True(childLog.Seen[1].Event.Is<Spawned>());
        Assert.Equal(5, childLog.Seen[2].Event.As<Ping>().Value);
        Assert.All(childLog.Seen, s => Assert.Equal(1, s.Tick));
        Assert.Equal(0, world.Stats.FallbackSpawns);
    }

    [Fact]
    public void ARunDryPoolFallsBackToAGlobalReservationAndSaysSo()
    {
        var world = new World(new WorldOptions { SpawnPoolSize = 2, MaxDegreeOfParallelism = 1 });
        using (world)
        {
            FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static));
            var log = new List<string>();
            world.Log += log.Add;
            var spawned = new List<Entity>();
            world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            {
                if (self.Tick == 0)
                    for (int i = 0; i < 5; i++)
                        spawned.Add(self.Spawn(new SpawnDesc { Frame = self.Frame }));
            }));

            world.Tick();

            Assert.Equal(5, spawned.Count);
            Assert.All(spawned, e => Assert.True(world.IsAlive(e)));
            Assert.Equal(5, spawned.Select(e => e.Index).Distinct().Count());
            Assert.Equal(3, world.Stats.FallbackSpawns);
            Assert.Contains(log, l => l.Contains("no longer deterministic"));
        }
    }

    [Fact]
    public void AnEntityCanDestroyItself()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity e = world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick == 2)
                self.Destroy();
        }));

        world.Run(2);
        Assert.True(world.IsAlive(e));
        world.Tick();
        Assert.False(world.IsAlive(e));
    }

    [Fact]
    public void ADestroyMessageDestroysTheRecipientUnlessAComponentRemovesIt()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity plain = world.SpawnWith(root);
        var watcher = new Recorder(EventMask.Of<Destroy>());
        Entity watched = world.SpawnWith(root, watcher);
        var shield = new Shield();
        Entity shielded = world.SpawnWith(root, shield, new Recorder(EventMask.Of<Destroy>(), priority: 5));
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            outbox.Send(plain, new Destroy());
            outbox.Send(watched, new Destroy());
            outbox.Send(shielded, new Destroy());
        }));

        world.Tick();
        Assert.True(world.IsAlive(plain));
        world.Tick();

        Assert.False(world.IsAlive(plain));
        Assert.False(world.IsAlive(watched));
        Assert.Equal(1, watcher.Runs);
        Assert.True(world.IsAlive(shielded));
        Assert.Equal(1, shield.Blocked);
        // The later component never saw the Destroy that the shield removed.
        Assert.Equal(0, world.GetComponent<Recorder>(shielded)!.Runs);
        world.Run(3);
        Assert.True(world.IsAlive(shielded));
        Assert.Equal(4, shield.Blocked);
    }

    [Fact]
    public void FreedSlotsBumpTheGenerationAndStaleHandlesAreRejectedEverywhere()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Topic topic = Topics.Get("tests.lifecycle.topic");
        Entity old = world.SpawnWith(root, new Recorder());
        Assert.Equal(1u, old.Generation);
        world.Destroy(old);
        Entity reused = world.SpawnWith(root, new Recorder());
        Assert.Equal(old.Index, reused.Index);
        Assert.Equal(old.Generation + 1, reused.Generation);

        Assert.False(world.IsAlive(old));
        Assert.False(world.IsAlive(Entity.Null));
        Assert.False(world.IsAlive(new Entity(100_000, 1)));
        Assert.Throws<InvalidOperationException>(() => world.GetPose(old));
        Assert.Throws<InvalidOperationException>(() => world.GetFrame(old));
        Assert.Throws<InvalidOperationException>(() => world.GetVelocity(old));
        Assert.Throws<InvalidOperationException>(() => world.GetComponents(old));
        Assert.Throws<InvalidOperationException>(() => world.GetComponent<Recorder>(old));
        Assert.Throws<InvalidOperationException>(() => world.Destroy(old));
        Assert.Throws<InvalidOperationException>(() => world.AddComponent(old, new Recorder()));
        Assert.Throws<InvalidOperationException>(() => world.Subscribe(old, topic));
        Assert.Throws<InvalidOperationException>(() => world.Snapshot.PoseOf(old));
        Assert.Throws<InvalidOperationException>(() => world.Snapshot.FrameOf(old));
        Assert.Throws<InvalidOperationException>(() => world.Snapshot.VelocityOf(old));
        Assert.Throws<InvalidOperationException>(() => world.GetPose(Entity.Null));
        Assert.False(world.Snapshot.TryGetPose(old, out _, out _));
        Assert.False(world.Snapshot.TryGetPoseIn(old, root, out _));
        Assert.True(world.Snapshot.TryGetPose(reused, out FrameHandle frame, out _));
        Assert.Equal(root, frame);
        Assert.Throws<ArgumentException>(() => world.Spawn(new SpawnDesc { Frame = root, AttachTo = old }));
    }

    [Fact]
    public void GenerationsBumpForSlotsFreedAtTheSyncPoint()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var made = new List<Entity>();
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            made.Add(self.Spawn(new SpawnDesc
            {
                Frame = self.Frame,
                Components = [new Lambda(EventMask.Of<Spawned>(), (Self s, scoped ref EventList e, scoped ref Outbox o) => s.Destroy())],
            }));
        }));

        world.Run(40);

        // Every spawned entity lived exactly one tick, and slots were reused with new generations.
        Assert.Equal(40, made.Count);
        Assert.Equal(39, made.Count(e => !world.IsAlive(e)));
        Assert.Contains(made.GroupBy(e => e.Index), g => g.Count() > 1);
        foreach (IGrouping<uint, Entity> slot in made.GroupBy(e => e.Index))
            Assert.Equal(slot.Count(), slot.Select(e => e.Generation).Distinct().Count());
        Assert.Equal(0, world.Stats.FallbackSpawns);
    }

    [Fact]
    public void SpawnValidation()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle doomed = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static) }));
        world.Destroy(doomed.Entity);

        Assert.Throws<ArgumentException>(() => world.Spawn(new SpawnDesc()));
        Assert.Throws<ArgumentException>(() => world.Spawn(new SpawnDesc { Frame = doomed }));
        Assert.Throws<ArgumentNullException>(() => world.Spawn((SpawnDesc)null!));
        Entity parent = world.SpawnWith(root);
        Assert.Throws<ArgumentException>(() => world.Spawn(new SpawnDesc { Frame = root, AttachTo = parent, Body = TestWorld.Ball }));
        Assert.Throws<ArgumentException>(() => world.Spawn(new SpawnDesc { Frame = root, Components = [null!] }));
        Assert.Throws<ArgumentException>(() => world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(default, MotionDriver.Static) }));
    }

    [Fact]
    public void WorldSpawnAndDestroyAreRejectedDuringATick()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Exception? caught = null;
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            try
            {
                world.Spawn(new SpawnDesc { Frame = root });
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }
        }));
        world.Tick();
        Assert.NotNull(caught);
    }

    [Fact]
    public void DestroyingAFrameMovesItsContentsToTheParentFrame()
    {
        var physics = new FakePhysics();
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        DQuaternion quarterTurn = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = root,
            Pose = new DPose(new Double3(1000, 0, 0), quarterTurn),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        var crewLog = new Recorder(EventMask.Of<FrameChanged>());
        Entity crew = world.Spawn(new SpawnDesc { Frame = ship, Pose = TestWorld.At(10), Body = TestWorld.Ball, Components = [crewLog] });
        Entity cargo = world.SpawnAt(ship, new Double3(0, 0, 10));
        FrameHandle pod = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = ship, Pose = TestWorld.At(-20), AsFrame = new FrameSpec(FrameBounds.Sphere(5), MotionDriver.Static) }));
        Entity passenger = world.SpawnAt(pod, new Double3(1, 2, 3));
        world.Tick();
        physics.Changes.Clear();

        world.Destroy(shipOwner);

        Assert.False(world.Frames.IsAlive(ship));
        Assert.Equal(root, world.GetFrame(crew));
        Assert.Equal(root, world.GetFrame(cargo));
        Assert.Equal(root, world.Frames.Parent(pod));
        Assert.Equal(root, world.GetFrame(pod.Entity));
        Assert.Equal(pod, world.GetFrame(passenger));
        // A quarter turn about +Y maps local +X to -Z and local +Z to +X.
        Assert.True(TestWorld.RoughlyEqual(new Double3(1000, 0, -10), world.GetPose(crew).Position), world.GetPose(crew).ToString());
        Assert.True(TestWorld.RoughlyEqual(new Double3(1010, 0, 0), world.GetPose(cargo).Position), world.GetPose(cargo).ToString());
        Assert.True(TestWorld.RoughlyEqual(new Double3(1000, 0, 20), world.GetPose(pod.Entity).Position));
        Assert.Contains($"body- {crew}", physics.Changes);
        Assert.Contains($"body+ {crew} in {root.Index}", physics.Changes);
        Assert.Contains($"frame- {ship.Index}", physics.Changes);
        Assert.True(physics.Changes.IndexOf($"body- {crew}") < physics.Changes.IndexOf($"body+ {crew} in {root.Index}"));

        // Like a posted message, the FrameChanged of an outside-tick destroy joins the next routing.
        world.Tick();
        Assert.Empty(crewLog.Seen);
        world.Tick();
        FrameChanged changed = crewLog.Seen.Single().Event.As<FrameChanged>();
        Assert.Equal(ship, changed.From);
        Assert.Equal(root, changed.To);
        Assert.Empty(world.Errors);
    }

    [Fact]
    public void DestroyingARootFrameDestroysItsContents()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle inner = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static) }));
        Entity a = world.SpawnWith(root);
        Entity b = world.SpawnWith(inner);
        int before = world.Stats.LiveEntities;
        Assert.Equal(4, before);

        world.Destroy(root.Entity);

        Assert.False(world.IsAlive(a));
        Assert.False(world.IsAlive(b));
        Assert.False(world.Frames.IsAlive(inner));
        Assert.Equal(0, world.Stats.LiveEntities);
        Assert.Equal(0, world.Frames.Count);
    }

    [Fact]
    public void NestedFramesThatDieTogetherHandTheirContentsToTheNearestSurvivor()
    {
        using World world = TestWorld.Create(out FrameHandle root, chunkSize: 1);
        FrameHandle outer = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(100), AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static) }));
        FrameHandle inner = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = outer, Pose = TestWorld.At(10), AsFrame = new FrameSpec(FrameBounds.Sphere(5), MotionDriver.Static) }));
        Entity x = world.SpawnAt(inner, new Double3(1, 0, 0));
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick != 0)
                return;
            outbox.Send(inner.Entity, new Destroy());
            outbox.Send(outer.Entity, new Destroy());
        }));

        world.Run(2);

        Assert.False(world.Frames.IsAlive(outer));
        Assert.False(world.Frames.IsAlive(inner));
        Assert.True(world.IsAlive(x));
        Assert.Equal(root, world.GetFrame(x));
        Assert.True(TestWorld.RoughlyEqual(new Double3(111, 0, 0), world.GetPose(x).Position));
        Assert.Empty(world.Errors);
    }

    [Fact]
    public void ASpawnIntoAFrameThatDiesInTheSameTickIsDropped()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle doomed = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
            Components = [new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.Destroy())],
        }));
        Entity child = default;
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick == 0)
                child = self.Spawn(new SpawnDesc { Frame = doomed });
        }));

        world.Tick();

        Assert.False(world.Frames.IsAlive(doomed));
        Assert.False(world.IsAlive(child));
        Assert.Contains(world.Errors, e => e.Contains("dropped the spawn"));
    }

    [Fact]
    public void ARootFrameOwnerHasNoFrameAndItsPoseIsTheFramePose()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Assert.Equal(FrameHandle.Null, world.GetFrame(root.Entity));
        Assert.Equal(root, world.GetOwnedFrame(root.Entity));
        Assert.Equal(DPose.Identity, world.GetPose(root.Entity));
        Assert.True(world.Snapshot.TryGetPoseIn(root.Entity, root, out DPose pose));
        Assert.Equal(Double3.Zero, pose.Position);
        Assert.Equal(Vector3.Zero, world.GetVelocity(root.Entity));
    }
}
