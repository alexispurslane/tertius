using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Creating entities, outside a tick and from inside one. An entity made outside a tick is live at once. An entity made
/// by a component during a tick gets its final handle at once, from its chunk's pool of reserved slots, but becomes
/// live only at the sync point, so it runs for the first time on the next tick, and its first run lists
/// <see cref="Spawned"/> before any message.
/// </summary>
public class SpawnTests
{
    /// <summary>The first entity that a component spawns in a fresh world: slot 0 is the root, slot 1 the spawner, so the pool starts at slot 2.</summary>
    private const uint FirstPoolSlot = 2;

    // ---- From inside a tick ---------------------------------------------------------------------------------------

    [Fact]
    public void AnEntitySpawnedDuringATickIsNotAliveUntilTheSyncPoint()
    {
        using var t = new TestWorld();
        Entity child = default;
        bool aliveDuringTheTick = true;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 0)
                return;
            child = self.Spawn(new SpawnDesc { Frame = self.Frame });
            aliveDuringTheTick = self.Snapshot.IsAlive(child);
        }));

        t.Run(1);

        aliveDuringTheTick.Should().BeFalse();
        t.World.IsAlive(child).Should().BeTrue();
    }

    [Fact]
    public void TheHandleOfASpawnDuringATickNamesTheEntityThatWasDescribed()
    {
        using var t = new TestWorld();
        var childLog = new Recorder();
        Entity child = default;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                child = self.Spawn(new SpawnDesc { Frame = self.Frame, Pose = TestWorld.At(3), Components = [childLog] });
        }));

        t.Run(1);

        t.World.GetComponent<Recorder>(child).Should().BeSameAs(childLog);
        t.World.PoseInOwnFrame(child).Should().Be(TestWorld.At(3));
    }

    [Fact]
    public void AnEntitySpawnedDuringATickIsInTheFrameOfItsDescription()
    {
        using var t = new TestWorld();
        FrameHandle bay = t.SpawnStaticFrame(TestWorld.At(500));
        Entity child = default;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                child = self.Spawn(new SpawnDesc { Frame = bay });
        }));

        t.Run(1);

        t.World.GetFrame(child).Should().Be(bay);
    }

    [Fact]
    public void AnEntitySpawnedDuringATickRunsForTheFirstTimeOnTheNextTick()
    {
        using var t = new TestWorld();
        var childLog = new Recorder(EventMask.Of<Tick>());
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                self.Spawn(new SpawnDesc { Frame = self.Frame, Components = [childLog] });
        }));

        t.Run(1);
        childLog.Runs.Should().Be(0);
        t.Run(1);

        childLog.History.Select(run => run.Tick).Should().Equal(1L);
    }

    [Fact]
    public void AMessageSentToASpawnedEntityInTheTickOfItsSpawnReachesItsFirstRun()
    {
        using var t = new TestWorld();
        var childLog = new Recorder(EventMask.Of<Ping>());
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 0)
                return;
            Entity child = self.Spawn(new SpawnDesc { Frame = self.Frame, Components = [childLog] });
            outbox.Send(child, new Ping { Value = 5 });
        }));

        t.Run(2);

        childLog.Pings().Should().Equal((1L, 5));
    }

    [Fact]
    public void TheFirstRunOfANewEntityListsSpawnedBeforeAnyMessage()
    {
        using var t = new TestWorld();
        var childLog = new Recorder(EventMask.Of<Spawned>().And<Ping>());
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 0)
                return;
            Entity child = self.Spawn(new SpawnDesc { Frame = self.Frame, Components = [childLog] });
            outbox.Send(child, new Ping { Value = 1 });
            outbox.Send(child, new Ping { Value = 2 });
        }));

        t.Run(2);

        childLog.History.Should().ContainSingle().Which.EventTypes.Should().Equal(typeof(Spawned), typeof(Ping), typeof(Ping));
    }

    [Fact]
    public void TheFirstRunOfANewEntityListsTickThenSpawnedThenItsMessages()
    {
        // The scheduler builds the list in this fixed order: the tick, the spawn notice, then the inbox.
        using var t = new TestWorld();
        var childLog = new Recorder(EventMask.Of<Tick>().And<Spawned>().And<Ping>());
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 0)
                return;
            Entity child = self.Spawn(new SpawnDesc { Frame = self.Frame, Components = [childLog] });
            outbox.Send(child, new Ping { Value = 1 });
        }));

        t.Run(2);

        childLog.History[0].EventTypes.Should().Equal(typeof(Tick), typeof(Spawned), typeof(Ping));
    }

    [Fact]
    public void ANewEntityGetsSpawnedOnItsFirstRunOnly()
    {
        using var t = new TestWorld();
        var childLog = new Recorder(EventMask.Of<Tick>().And<Spawned>());
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                self.Spawn(new SpawnDesc { Frame = self.Frame, Components = [childLog] });
        }));

        t.Run(4);

        // The child runs on ticks 1, 2 and 3. Only the first of those runs has the notice.
        childLog.History.Select(run => run.EventTypes.Contains(typeof(Spawned))).Should().Equal(true, false, false);
    }

    [Fact]
    public void AnEntityThatDoesNotWantSpawnedNeverSeesIt()
    {
        using var t = new TestWorld();
        var childLog = new Recorder(EventMask.Of<Tick>());
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                self.Spawn(new SpawnDesc { Frame = self.Frame, Components = [childLog] });
        }));

        t.Run(3);

        childLog.History.SelectMany(run => run.EventTypes).Should().OnlyContain(type => type == typeof(Tick));
    }

    [Fact]
    public void ASpawnerGivesEachChildItsOwnComponentsAndItsOwnSlotInSpawnOrder()
    {
        using var t = new TestWorld();
        var spawner = new Spawner(() => [new Recorder()], count: 3);
        t.SpawnWith(spawner);

        t.Run(1);

        spawner.Children.Should().HaveCount(3).And.OnlyContain(child => t.World.IsAlive(child));
        // The chunk's pool was reserved from fresh slots in order, and the children took them in order.
        spawner.Children.Select(child => child.Index).Should().Equal(FirstPoolSlot, FirstPoolSlot + 1, FirstPoolSlot + 2);
        spawner.Children.Select(child => child.Generation).Should().OnlyContain(generation => generation == 1u);
        spawner.Children.Select(child => t.World.GetComponent<Recorder>(child)).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void ASpawnerThatSpawnsEveryTickMakesANewEntityEveryTick()
    {
        using var t = new TestWorld();
        var spawner = new Spawner(every: 1);
        t.SpawnWith(spawner);

        t.Run(5);

        spawner.Children.Should().HaveCount(5).And.OnlyContain(child => t.World.IsAlive(child));
        spawner.Children.Select(child => child.Index).Distinct().Should().HaveCount(5);
    }

    [Fact]
    public void AnEntityDestroyedByItsSpawnerInTheTickOfItsSpawnStillGetsItsFirstRun()
    {
        using var t = new TestWorld();
        var childLog = new Recorder(EventMask.Of<Spawned>().And<Destroy>());
        Entity child = default;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 0)
                return;
            child = self.Spawn(new SpawnDesc { Frame = self.Frame, Components = [childLog] });
            outbox.Send(child, new Destroy());
        }));

        t.Run(2);

        childLog.History.Should().ContainSingle().Which.EventTypes.Should().Equal(typeof(Spawned), typeof(Destroy));
        t.World.IsAlive(child).Should().BeFalse();
    }

    [Fact]
    public void AChildSpawnedByAnEntityThatDestroysItselfInTheSameTickBecomesLive()
    {
        using var t = new TestWorld();
        Entity child = default;
        Entity parent = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            child = self.Spawn(new SpawnDesc { Frame = self.Frame });
            self.Destroy();
        }));

        t.Run(1);

        t.World.IsAlive(parent).Should().BeFalse();
        t.World.IsAlive(child).Should().BeTrue();
    }

    [Fact]
    public void AFrameSpawnedDuringATickExistsAfterTheTickAndTakesEntitiesFromTheNextOne()
    {
        using var t = new TestWorld();
        Entity bay = default, visitor = default;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
            {
                bay = self.Spawn(new SpawnDesc
                {
                    Frame = self.Frame,
                    Pose = TestWorld.At(500),
                    AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static),
                });
            }
            else if (self.Tick == 1 && self.Snapshot.Frames.TryGetFrame(bay, out FrameHandle frame))
            {
                visitor = self.Spawn(new SpawnDesc { Frame = frame });
            }
        }));

        t.Run(3);

        FrameHandle frame = t.World.GetOwnedFrame(bay);
        t.World.Frames.IsAlive(frame).Should().BeTrue();
        t.World.GetFrame(visitor).Should().Be(frame);
    }

    // ---- From outside a tick --------------------------------------------------------------------------------------

    [Fact]
    public void AnEntitySpawnedOutsideATickIsAliveAtOnce()
    {
        using var t = new TestWorld();

        Entity entity = t.SpawnWith();

        t.World.IsAlive(entity).Should().BeTrue();
    }

    [Fact]
    public void AnEntitySpawnedOutsideATickHasItsPoseAndFrameAtOnce()
    {
        using var t = new TestWorld();

        Entity entity = t.SpawnAt(new Double3(3, 4, 5));

        t.World.PoseInOwnFrame(entity).Position.Should().Be(new Double3(3, 4, 5));
        t.World.GetFrame(entity).Should().Be(t.Root);
    }

    [Fact]
    public void AnEntitySpawnedOutsideATickDoesNotRunUntilTheNextTick()
    {
        using var t = new TestWorld();
        var log = new Recorder(EventMask.Of<Spawned>());

        t.SpawnWith(log);

        log.Runs.Should().Be(0);
    }

    [Fact]
    public void AnEntitySpawnedOutsideATickGetsSpawnedOnTheNextTick()
    {
        using var t = new TestWorld();
        var log = new Recorder(EventMask.Of<Spawned>());
        t.SpawnWith(log);

        t.Run(2);

        log.History.Should().ContainSingle();
        log.History[0].Tick.Should().Be(0);
        log.History[0].EventTypes.Should().Equal(typeof(Spawned));
    }

    [Fact]
    public void EntitiesSpawnedOutsideATickTakeSlotsInSpawnOrder()
    {
        using var t = new TestWorld();

        Entity[] entities = [t.SpawnWith(), t.SpawnWith(), t.SpawnWith()];

        // Slot 0 is the root frame's owner.
        entities.Select(e => e.Index).Should().Equal(1u, 2u, 3u);
    }

    [Fact]
    public void AnEntityDestroyedBeforeTheFirstTickNeverRuns()
    {
        using var t = new TestWorld();
        var log = new Recorder(EventMask.Of<Spawned>().And<Tick>());
        Entity entity = t.SpawnWith(log);

        t.World.Destroy(entity);
        t.Run(2);

        log.Runs.Should().Be(0);
    }

    // ---- Physics hears of a body ----------------------------------------------------------------------------------

    [Fact]
    public void ABodySpawnedDuringATickReachesPhysicsAtTheSyncPointOfThatTick()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<ChangeBatch> batches = physics.RecordChanges();
        using var t = new TestWorld(physics);
        Entity child = default;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                child = self.Spawn(new SpawnDesc { Frame = self.Frame, Body = TestWorld.Ball });
        }));

        t.Run(1);

        BodyCreate create = batches.SelectMany(batch => batch.BodiesCreated).Should().ContainSingle().Subject;
        create.Entity.Should().Be(child);
        create.Frame.Should().Be(t.Root);
    }

    [Fact]
    public void ABodySpawnedOutsideATickReachesPhysicsBeforeTheFirstStep()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<ChangeBatch> batches = physics.RecordChanges();
        var batchesAppliedAtEachStep = new List<int>();
        physics.OnStep((_, _) => batchesAppliedAtEachStep.Add(batches.Count));
        using var t = new TestWorld(physics);
        Entity ball = t.World.Spawn(new SpawnDesc { Frame = t.Root, Body = TestWorld.Ball });

        t.Run(1);

        batches[0].BodiesCreated.Select(create => create.Entity).Should().Equal(ball);
        // One step ran, and the batch with the ball had been applied when it began.
        batchesAppliedAtEachStep.Should().Equal(1);
    }

    [Fact]
    public void AnEntityWithoutABodyIsNeverCreatedInPhysics()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<ChangeBatch> batches = physics.RecordChanges();
        using var t = new TestWorld(physics);
        t.SpawnWith(new Recorder());

        t.Run(2);

        batches.SelectMany(batch => batch.BodiesCreated).Should().BeEmpty();
    }

    // ---- The pool of reserved slots -------------------------------------------------------------------------------

    [Fact]
    public void ASpawnerThatStaysWithinItsChunksPoolNeverFallsBack()
    {
        using var t = new TestWorld();
        t.SpawnWith(new Spawner(count: 5));

        t.Run(1);

        t.World.Stats.FallbackSpawns.Should().Be(0);
    }

    [Fact]
    public void ASpawnerThatOutrunsItsPoolStillGetsLiveEntitiesWithDistinctHandles()
    {
        using var t = new TestWorld(configure: options => options.SpawnPoolSize = 2);
        var spawner = new Spawner(count: 5);
        t.SpawnWith(spawner);

        t.Run(1);

        spawner.Children.Should().OnlyContain(child => t.World.IsAlive(child));
        // The pool held slots 2 and 3, and the three spawns after that took the next fresh slots, in order.
        spawner.Children.Select(child => child.Index).Should().Equal(2u, 3u, 4u, 5u, 6u);
    }

    [Fact]
    public void ASpawnerThatOutrunsItsPoolIsCountedAsFallbackSpawns()
    {
        using var t = new TestWorld(configure: options => options.SpawnPoolSize = 2);
        t.SpawnWith(new Spawner(count: 5));

        t.Run(1);

        // Five spawns, two of them from the pool.
        t.World.Stats.FallbackSpawns.Should().Be(3);
    }

    [Fact]
    public void ASpawnerThatOutrunsItsPoolWarnsThatTheRunIsNoLongerDeterministic()
    {
        using var t = new TestWorld(configure: options => options.SpawnPoolSize = 2);
        var log = new List<string>();
        t.World.Log += log.Add;
        t.SpawnWith(new Spawner(count: 5));

        t.Run(1);

        log.Should().ContainSingle(line => line.Contains("no longer deterministic"));
    }

    [Fact]
    public void AChunkThatSpawnedManyEntitiesGetsAPoolBigEnoughForTheNextTick()
    {
        using var t = new TestWorld(configure: options => options.SpawnPoolSize = 2);
        t.SpawnWith(new Spawner(count: 5, every: 1));

        t.Run(4);

        // Only the first tick falls back, by three spawns. After that the pool is twice what the chunk spawned.
        t.World.Stats.FallbackSpawns.Should().Be(3);
    }

    [Fact]
    public void ASpawnPoolSizeOfZeroSendsEverySpawnToTheFallback()
    {
        using var t = new TestWorld(configure: options => options.SpawnPoolSize = 0);
        var spawner = new Spawner(count: 2, every: 1);
        t.SpawnWith(spawner);

        t.Run(3);

        spawner.Children.Should().HaveCount(6).And.OnlyContain(child => t.World.IsAlive(child));
        t.World.Stats.FallbackSpawns.Should().Be(6);
    }

    // ---- Slots that are freed and used again ----------------------------------------------------------------------

    /// <summary>A world with an entity that spawns one child per tick, and children that destroy themselves on their first run.</summary>
    private static List<Entity> RunWithShortLivedChildren(TestWorld t, int ticks)
    {
        var made = new List<Entity>();
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            made.Add(self.Spawn(new SpawnDesc
            {
                Frame = self.Frame,
                Components = [new Lambda(EventMask.Of<Spawned>(), (child, scoped ref childEvents, scoped ref childOutbox) => child.Destroy())],
            }));
        }));
        t.Run(ticks);
        return made;
    }

    [Fact]
    public void ASlotFreedAtTheSyncPointIsUsedAgainByALaterSpawn()
    {
        using var t = new TestWorld();

        List<Entity> made = RunWithShortLivedChildren(t, 40);

        made.GroupBy(e => e.Index).Should().Contain(slot => slot.Count() > 1);
    }

    [Fact]
    public void EachTimeASlotIsUsedAgainItsGenerationIsOneHigher()
    {
        using var t = new TestWorld();

        List<Entity> made = RunWithShortLivedChildren(t, 40);

        foreach (IGrouping<uint, Entity> slot in made.GroupBy(e => e.Index))
            slot.Select(e => e.Generation).Should().Equal(Enumerable.Range(1, slot.Count()).Select(g => (uint)g));
    }

    [Fact]
    public void OnlyTheNewestOfTheShortLivedChildrenIsStillAlive()
    {
        using var t = new TestWorld();

        List<Entity> made = RunWithShortLivedChildren(t, 40);

        // The child of the last tick has not had its first run yet; every earlier child destroyed itself on that run.
        made.Where(e => t.World.IsAlive(e)).Should().Equal(made[^1]);
    }

    [Fact]
    public void ReusingSlotsDoesNotNeedTheFallback()
    {
        using var t = new TestWorld();

        RunWithShortLivedChildren(t, 40);

        t.World.Stats.FallbackSpawns.Should().Be(0);
    }
}
