using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// How an entity ends. An entity can ask for its own end with <see cref="Self.Destroy"/>, or receive a
/// <see cref="Destroy"/> message from another entity. Either way, it dies at the sync point of the tick in which it ran:
/// an entity whose list still holds a <see cref="Destroy"/> when its last component has run is destroyed there. A
/// component can refuse by removing the event (the "veto"), and a message that arrives on tick N+1 was sent on tick N.
/// Frames have their own tests in <see cref="LifecycleFrameTests"/>.
/// </summary>
public class LifecycleTests
{
    // ---- An entity ends itself ------------------------------------------------------------------------------------

    [Fact]
    public void AnEntityThatDestroysItselfLivesUntilTheEndOfThatTick()
    {
        using var t = new TestWorld();
        Entity entity = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 2)
                self.Destroy();
        }));

        t.Run(2);
        t.World.IsAlive(entity).Should().BeTrue();
        t.Run(1);

        t.World.IsAlive(entity).Should().BeFalse();
    }

    [Fact]
    public void AnEntityThatDestroysItselfStillRunsItsLaterComponentsThatTick()
    {
        using var t = new TestWorld();
        var later = new Recorder(EventMask.Of<Tick>(), priority: 5);
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy()), later);

        t.Run(1);

        later.Runs.Should().Be(1);
    }

    [Fact]
    public void AnEntityThatDestroysItselfNeverRunsAgain()
    {
        using var t = new TestWorld();
        var recorder = new Recorder(EventMask.Of<Tick>());
        t.SpawnWith(recorder, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 2)
                self.Destroy();
        }, priority: 1));

        t.Run(6);

        recorder.History.Select(run => run.Tick).Should().Equal(0L, 1L, 2L);
    }

    // ---- A Destroy message ----------------------------------------------------------------------------------------

    [Fact]
    public void ADestroyMessageDestroysItsRecipientOnTheTickItArrives()
    {
        using var t = new TestWorld();
        Entity plain = t.SpawnWith();
        t.SpawnWith(SendOnce.Of(plain, new Destroy()));

        t.Run(1);
        t.World.IsAlive(plain).Should().BeTrue("the message is sent on tick 0 and arrives on tick 1");
        t.Run(1);

        t.World.IsAlive(plain).Should().BeFalse();
    }

    [Fact]
    public void AComponentThatWantsDestroyHandlesItBeforeTheEntityDies()
    {
        using var t = new TestWorld();
        var watcher = new Recorder(EventMask.Of<Destroy>());
        Entity watched = t.SpawnWith(watcher);
        t.SpawnWith(SendOnce.Of(watched, new Destroy()));

        t.Run(2);

        watcher.Runs.Should().Be(1);
        t.World.IsAlive(watched).Should().BeFalse();
    }

    [Fact]
    public void ADestroyMessageToADeadEntityIsIgnored()
    {
        using var t = new TestWorld();
        Entity doomed = t.SpawnWith();
        t.World.Destroy(doomed);
        t.SpawnWith(SendOnce.Of(doomed, new Destroy()));

        t.Run(3);

        t.World.Errors.Should().BeEmpty();
    }

    // ---- The veto -------------------------------------------------------------------------------------------------

    [Fact]
    public void AComponentThatRemovesTheDestroyMessageKeepsTheEntityAlive()
    {
        using var t = new TestWorld();
        var shield = new Invulnerable();
        Entity shielded = t.SpawnWith(shield);
        t.SpawnWith(SendOnce.Of(shielded, new Destroy()));

        t.Run(3);

        t.World.IsAlive(shielded).Should().BeTrue();
        shield.Blocked.Should().Be(1);
    }

    [Fact]
    public void AVetoHidesTheDestroyMessageFromTheComponentsAfterIt()
    {
        using var t = new TestWorld();
        var after = new Recorder(EventMask.Of<Destroy>(), priority: 5);
        Entity shielded = t.SpawnWith(new Invulnerable(priority: -10), after);
        t.SpawnWith(SendOnce.Of(shielded, new Destroy()));

        t.Run(3);

        after.Runs.Should().Be(0);
    }

    [Fact]
    public void AComponentBeforeTheVetoStillSeesTheDestroyMessage()
    {
        using var t = new TestWorld();
        var before = new Recorder(EventMask.Of<Destroy>(), priority: -20);
        Entity shielded = t.SpawnWith(before, new Invulnerable(priority: -10));
        t.SpawnWith(SendOnce.Of(shielded, new Destroy()));

        t.Run(3);

        before.Runs.Should().Be(1);
        t.World.IsAlive(shielded).Should().BeTrue();
    }

    [Fact]
    public void AVetoStaysInForceForEveryLaterDestroyMessage()
    {
        using var t = new TestWorld();
        var shield = new Invulnerable();
        Entity shielded = t.SpawnWith(shield);
        t.SpawnWith(SendEvery.Of(shielded, new Destroy()));

        t.Run(5);

        // Sent on ticks 0 to 4; the ones sent on ticks 0 to 3 have arrived and been refused.
        shield.Blocked.Should().Be(4);
        t.World.IsAlive(shielded).Should().BeTrue();
    }

    [Fact]
    public void AVetoProtectsOnlyTheEntityThatHasIt()
    {
        using var t = new TestWorld();
        Entity shielded = t.SpawnWith(new Invulnerable());
        Entity exposed = t.SpawnWith();
        t.SpawnWith(SendOnce.Of(shielded, new Destroy()), SendOnce.Of(exposed, new Destroy()));

        t.Run(2);

        t.World.IsAlive(shielded).Should().BeTrue();
        t.World.IsAlive(exposed).Should().BeFalse();
    }

    // ---- A Destroy that a component adds --------------------------------------------------------------------------

    [Fact]
    public void FatalDamageDestroysAnEntityWithHealth()
    {
        using var t = new TestWorld();
        Entity target = t.SpawnWith(new Health());
        t.SpawnWith(SendOnce.Of(target, new Damage { Amount = 100 }));

        t.Run(2);

        t.World.IsAlive(target).Should().BeFalse();
    }

    [Fact]
    public void DamageThatLeavesHealthAboveZeroLeavesTheEntityAlive()
    {
        using var t = new TestWorld();
        var health = new Health();
        Entity target = t.SpawnWith(health);
        t.SpawnWith(SendOnce.Of(target, new Damage { Amount = 40 }));

        t.Run(2);

        t.World.IsAlive(target).Should().BeTrue();
        health.Current.Should().Be(60f);
    }

    [Fact]
    public void AVetoThatRunsAfterHealthKeepsAnEntityAliveThroughFatalDamage()
    {
        using var t = new TestWorld();
        var health = new Health();
        var shield = new Invulnerable(priority: 10);
        Entity target = t.SpawnWith(health, shield);
        t.SpawnWith(SendOnce.Of(target, new Damage { Amount = 150 }));

        t.Run(2);

        t.World.IsAlive(target).Should().BeTrue();
        health.Current.Should().Be(-50f);
        shield.Blocked.Should().Be(1);
    }

    [Fact]
    public void AVetoThatRunsBeforeHealthCannotRemoveTheDestroyThatHealthAdds()
    {
        using var t = new TestWorld();
        var shield = new Invulnerable(priority: -10);
        Entity target = t.SpawnWith(new Health(), shield);
        t.SpawnWith(SendOnce.Of(target, new Damage { Amount = 150 }));

        t.Run(2);

        t.World.IsAlive(target).Should().BeFalse();
        shield.Blocked.Should().Be(0);
    }

    // ---- World.Destroy --------------------------------------------------------------------------------------------

    [Fact]
    public void DestroyingALiveEntityOutsideATickKillsItAtOnce()
    {
        using var t = new TestWorld();
        Entity entity = t.SpawnWith();
        int liveBefore = t.World.Stats.LiveEntities;

        bool destroyed = t.World.Destroy(entity);

        destroyed.Should().BeTrue();
        t.World.IsAlive(entity).Should().BeFalse();
        t.World.Stats.LiveEntities.Should().Be(liveBefore - 1);
    }

    [Fact]
    public void DestroyReturnsFalseForAnEntityThatDiedDuringATick()
    {
        using var t = new TestWorld();
        Entity entity = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy()));
        t.Run(1);

        t.World.Destroy(entity).Should().BeFalse();
    }

    [Fact]
    public void DestroyingAnEntityLeavesTheOthersAlone()
    {
        using var t = new TestWorld();
        Entity doomed = t.SpawnWith();
        Entity bystander = t.SpawnAt(new(4, 0, 0), new Recorder());

        t.World.Destroy(doomed);

        t.World.IsAlive(bystander).Should().BeTrue();
        t.World.PoseInOwnFrame(bystander).Position.Should().Be(new Double3(4, 0, 0));
    }

    // ---- Order ----------------------------------------------------------------------------------------------------

    [Fact]
    public void EntitiesInDifferentChunksThatDestroyThemselvesInOneTickFreeTheirSlotsInChunkOrder()
    {
        // One slot per chunk, so that the three entities are in three chunks. The sync point destroys in chunk order, and
        // the free list gives slots back in the order they were freed, so the next three spawns get the same handles in
        // every run.
        using var t = new TestWorld(chunkSize: 1);
        Entity[] doomed =
        [
            .. Enumerable.Range(0, 3).Select(_ =>
                t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy())))
        ];
        t.Run(1);

        Entity[] next = [t.SpawnWith(), t.SpawnWith(), t.SpawnWith()];

        next.Select(e => e.Index).Should().Equal(doomed.Select(e => e.Index));
        next.Select(e => e.Generation).Should().OnlyContain(generation => generation == 2u);
    }

    // ---- Physics hears of the end of a body -----------------------------------------------------------------------

    [Fact]
    public void DestroyingAnEntityWithABodyOutsideATickTellsPhysicsToDropTheBody()
    {
        using TestWorld t = TestWorld.OnJolt(out FaultInjectingBackend faults);
        List<PhysicsChanges> batches = faults.AppliedChanges;
        Entity ball = t.World.Spawn(new() { Frame = t.Root, Body = TestWorld.Ball });
        t.Run(1);
        batches.Clear();

        t.World.Destroy(ball);

        batches.Should().ContainSingle().Which.BodiesDestroyed.Should().Equal(ball);
    }

    [Fact]
    public void AnEntityWithABodyThatDestroysItselfIsDroppedFromPhysicsAtTheSyncPoint()
    {
        using TestWorld t = TestWorld.OnJolt(out FaultInjectingBackend faults);
        List<PhysicsChanges> batches = faults.AppliedChanges;
        Entity ball = t.World.Spawn(new()
        {
            Frame = t.Root,
            Body = TestWorld.Ball,
            Components =
            [
                Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick == 1)
                        self.Destroy();
                })
            ]
        });

        t.Run(2);

        batches.SelectMany(batch => batch.BodiesDestroyed).Should().Equal(ball);
    }

    [Fact]
    public void DestroyingAnEntityWithoutABodyTellsPhysicsNothingAboutBodies()
    {
        using TestWorld t = TestWorld.OnJolt(out FaultInjectingBackend faults);
        List<PhysicsChanges> batches = faults.AppliedChanges;
        Entity plain = t.SpawnWith();
        t.Run(1);

        t.World.Destroy(plain);

        batches.SelectMany(batch => batch.BodiesDestroyed).Should().BeEmpty();
    }
}
