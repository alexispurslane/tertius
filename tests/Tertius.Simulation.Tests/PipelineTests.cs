namespace Tertius.Simulation.Tests;

public class PipelineTests
{
    private static Lambda Named(List<string> log, string name, int priority, EventMask wants) =>
        new(wants, (Self self, scoped ref EventList events, scoped ref Outbox outbox) => log.Add(name), priority);

    [Fact]
    public void ComponentsRunInPriorityOrderAndTiesKeepInsertionOrder()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var log = new List<string>();
        EventMask tick = EventMask.Of<Tick>();
        world.SpawnWith(root,
            Named(log, "5a", 5, tick),
            Named(log, "1a", 1, tick),
            Named(log, "5b", 5, tick),
            Named(log, "1b", 1, tick),
            Named(log, "3", 3, tick),
            Named(log, "-2", -2, tick));

        world.Tick();

        Assert.Equal(["-2", "1a", "1b", "3", "5a", "5b"], log);
    }

    [Fact]
    public void AnAddedEventIsVisibleOnlyToLaterComponents()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var before = new Recorder(EventMask.Of<Tick>().And<Note>(), priority: -1);
        var adder = new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) => events.Add(new Note { Value = 42 }, self.Entity));
        var after = new Recorder(EventMask.Of<Note>(), priority: 1);
        Entity e = world.SpawnWith(root, before, adder, after);

        world.Tick();

        Assert.Single(before.Seen);
        Assert.True(before.Seen[0].Event.Is<Tick>());
        Assert.Equal(2, after.Seen.Count);
        Assert.True(after.Seen[0].Event.Is<Tick>());
        Assert.Equal(42, after.Seen[1].Event.As<Note>().Value);
        Assert.True(after.Seen[1].Event.Target.TryGetValue(out Entity target));
        Assert.Equal(e, target);
    }

    [Fact]
    public void ComponentsWhoseWantsDoNotOverlapAreSkipped()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var pings = new Recorder(EventMask.Of<Ping>());
        var heals = new Recorder(EventMask.Of<Heal>());
        Entity target = world.SpawnWith(root, pings, heals);
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(),
            (Self self, scoped ref EventList events, scoped ref Outbox outbox) => outbox.Send(target, new Ping { Value = 1 })));

        world.Run(2);

        Assert.Equal(1, pings.Runs);
        Assert.Equal(0, heals.Runs);
    }

    [Fact]
    public void AnEntityWithNoMatchingEventsDoesNotRun()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var idle = new Recorder(EventMask.Of<Ping>());
        Entity e = world.SpawnWith(root, idle);

        world.Run(5);
        Assert.Equal(0, idle.Runs);

        // A message type that the entity does not want is dropped at routing and does not wake it up.
        world.Post(Event.Create(e, Entity.Null, new Heal()));
        world.Run(2);
        Assert.Equal(0, idle.Runs);
        Assert.Equal(0, world.Stats.Deliveries);

        world.Post(Event.Create(e, Entity.Null, new Ping { Value = 3 }));
        world.Run(2);
        Assert.Equal(1, idle.Runs);
        Assert.Equal([3], idle.PingValues());
    }

    [Fact]
    public void TickEventCarriesTheTickNumberAndComesFirst()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var recorder = new Recorder(EventMask.Of<Tick>().And<Ping>().And<Spawned>());
        Entity e = world.SpawnWith(root, recorder);
        world.Post(Event.Create(e, Entity.Null, new Ping { Value = 1 }));

        world.Run(2);

        // Tick 0: Tick, then Spawned. Tick 1: Tick, then the posted Ping.
        Assert.Equal(4, recorder.Seen.Count);
        Assert.Equal(0, recorder.Seen[0].Event.As<Tick>().Number);
        Assert.Equal(world.DeltaTime, recorder.Seen[0].Event.As<Tick>().DeltaTime);
        Assert.True(recorder.Seen[1].Event.Is<Spawned>());
        Assert.Equal(1, recorder.Seen[2].Event.As<Tick>().Number);
        Assert.True(recorder.Seen[3].Event.Is<Ping>());
    }

    [Fact]
    public void SpawnedIsDeliveredOnceAndOnlyIfWanted()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var wants = new Recorder(EventMask.Of<Spawned>());
        var ignores = new Recorder(EventMask.Of<Ping>());
        world.SpawnWith(root, wants);
        world.SpawnWith(root, ignores);

        world.Run(3);

        Assert.Equal(1, wants.Runs);
        Assert.True(wants.Seen.Single().Event.Is<Spawned>());
        Assert.Equal(0, ignores.Runs);
    }

    [Fact]
    public void AComponentExceptionIsIsolatedAndReported()
    {
        using World world = TestWorld.Create(out FrameHandle root, maxDop: 4);
        var logged = new List<string>();
        world.Log += logged.Add;
        var thrower = new Lambda(EventMask.Of<Tick>(),
            (Self self, scoped ref EventList events, scoped ref Outbox outbox) => throw new InvalidOperationException("kaboom"));
        var later = new Recorder(EventMask.Of<Tick>(), priority: 1);
        Entity bad = world.SpawnWith(root, thrower, later);
        var bystander = new Recorder(EventMask.Of<Tick>());
        world.SpawnWith(root, bystander);

        world.Run(2);

        Assert.Equal(2, later.Runs);
        Assert.Equal(2, bystander.Runs);
        Assert.Equal(2, world.Errors.Count);
        Assert.Contains(bad.ToString(), world.Errors[0]);
        Assert.Contains(typeof(Lambda).FullName!, world.Errors[0]);
        Assert.Contains("kaboom", world.Errors[0]);
        Assert.Contains("tick 0", world.Errors[0]);
        Assert.Contains("tick 1", world.Errors[1]);
        Assert.Equal(2, logged.Count(l => l.Contains("kaboom")));
        Assert.True(world.IsAlive(bad));
    }

    [Fact]
    public void AddAndRemoveComponentTakeEffectForTheNextRunAndUpdateTheMask()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var late = new Recorder(EventMask.Of<Ping>());
        int phase = 0;
        bool visibleDuringRun = false;
        Entity target = default;
        var manager = new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick == 0)
            {
                self.AddComponent(late);
                visibleDuringRun |= self.GetComponent<Recorder>() != null;
            }
            if (self.Tick == 2)
                self.RemoveComponent(late);
            phase++;
        });
        target = world.SpawnWith(root, manager);
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(),
            (Self self, scoped ref EventList events, scoped ref Outbox outbox) => outbox.Send(target, new Ping { Value = (int)self.Tick })));

        world.Tick();
        // The Ping sent during tick 0 is routed with the new mask, so it reaches the new component on tick 1.
        Assert.Same(late, world.GetComponent<Recorder>(target));
        world.Run(4);

        Assert.Equal([(1L, 0), (2L, 1)], late.Pings());
        Assert.Null(world.GetComponent<Recorder>(target));
        Assert.Single(world.GetComponents(target));
        Assert.Equal(5, phase);
        Assert.False(visibleDuringRun);
        Assert.Empty(world.Errors);
    }

    [Fact]
    public void AnAddedComponentTakesItsPlaceByPriorityAfterEqualOnes()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var order = new List<string>();
        Lambda Named(string name, int priority) =>
            new(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) => order.Add(name), priority);
        Entity e = world.SpawnWith(root, Named("a0", 0), Named("b10", 10));

        world.AddComponent(e, Named("c5", 5));
        world.AddComponent(e, Named("d-1", -1));
        world.AddComponent(e, Named("e5", 5));
        world.Tick();

        Assert.Equal(["d-1", "a0", "c5", "e5", "b10"], order);
        Assert.Equal([-1, 0, 5, 5, 10], world.GetComponents(e).Select(c => c.Priority));
    }

    [Fact]
    public void ComponentChangesThatFailDuringATickAreReported()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var log = new List<string>();
        world.Log += log.Add;
        var stranger = new Recorder();
        Lambda? self = null;
        self = new Lambda(EventMask.Of<Tick>(), (Self s, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (s.Tick != 0)
                return;
            s.AddComponent(self!);
            s.RemoveComponent(stranger);
        });
        Entity e = world.SpawnWith(root, self);

        world.Tick();

        Assert.Contains("AddComponent", Assert.Single(world.Errors));
        Assert.Contains(log, l => l.Contains("RemoveComponent: the entity has no such Recorder"));
        Assert.Single(world.GetComponents(e));
    }

    [Fact]
    public void WorldAddComponentOutsideATick()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity e = world.SpawnWith(root);
        var recorder = new Recorder(EventMask.Of<Tick>());
        world.AddComponent(e, recorder);
        world.Tick();
        Assert.Equal(1, recorder.Runs);
        Assert.True(world.RemoveComponent(e, recorder));
        Assert.False(world.RemoveComponent(e, recorder));
        world.Tick();
        Assert.Equal(1, recorder.Runs);
        world.AddComponent(e, recorder);
        Assert.Throws<InvalidOperationException>(() => world.AddComponent(e, recorder));
    }

    [Fact]
    public void SelfLogReachesTheLogAndErrorsKeepOnlyTheNewest()
    {
        using var world = new World(new WorldOptions { MaxErrors = 3, MaxDegreeOfParallelism = 1 });
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        var log = new List<string>();
        world.Log += log.Add;
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            self.Log($"hello {self.Tick}");
            throw new InvalidOperationException($"failure {self.Tick}");
        }));

        world.Run(5);

        Assert.Equal(5, log.Count(l => l.Contains("hello")));
        Assert.Equal(5, log.Count(l => l.Contains("failure")));
        Assert.Equal(3, world.Errors.Count);
        Assert.Contains("failure 2", world.Errors[0]);
        Assert.Contains("failure 4", world.Errors[2]);
        world.ClearErrors();
        Assert.Empty(world.Errors);
    }

    [Fact]
    public void AComponentThatThrowsFromWantsFailsTheSpawnNotTheTick()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Assert.Throws<NotSupportedException>(() => world.SpawnWith(root, new BrokenWants()));
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick == 0)
                self.Spawn(new SpawnDesc { Frame = self.Frame, Components = [new BrokenWants()] });
        }));
        world.Run(2);
        Assert.Contains(nameof(NotSupportedException), Assert.Single(world.Errors));
        Assert.Equal(2, world.Stats.LiveEntities);
    }

    private sealed class BrokenWants : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => throw new NotSupportedException("no wants");

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
        }
    }

    [Fact]
    public void SharedComponentInstancesAreRejected()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var shared = new Recorder();
        Assert.Throws<ArgumentException>(() => world.Spawn(new SpawnDesc { Frame = root, Components = [shared, shared] }));
    }
}
