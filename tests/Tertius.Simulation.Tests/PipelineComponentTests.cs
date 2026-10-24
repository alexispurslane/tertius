using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The components of an entity as a list that changes, and as code that fails: adding and removing a component
/// from inside a run or from outside a tick, and what the world does when a component throws or declares its wants wrongly.
/// </summary>
public sealed class PipelineComponentTests
{
    private sealed class BrokenWants : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => throw new NotSupportedException("no wants");

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
        }
    }

    /// <summary>What a component does to its entity on one tick. A delegate of our own, because <see cref="Self"/> is a ref struct and cannot be a type argument of Action.</summary>
    private delegate void SelfAction(Self self);

    /// <summary>A component that, on one tick, runs <paramref name="change"/> against its entity.</summary>
    private static Lambda OnTick(long tick, SelfAction change) =>
        Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == tick)
                change(self);
        });

    // ---- Adding and removing during a run ---------------------------------------------------------------------------

    [Fact]
    public void AComponentAddedDuringARunJoinsTheEntityAtItsNextRun()
    {
        using var t = new TestWorld();
        var added = new Recorder(EventMask.Of<Tick>());
        Recorder? visibleDuringTheRun = added;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 0)
                return;
            self.AddComponent(added);
            visibleDuringTheRun = self.GetComponent<Recorder>();
        }));

        t.Run(3);

        // Not called on tick 0, when it was added, and called on every run after that.
        added.History.Select(run => run.Tick).Should().Equal(1, 2);
        visibleDuringTheRun.Should().BeNull();
    }

    [Fact]
    public void AMessageSentOnTheTickAComponentWasAddedAlreadyReachesTheNewComponent()
    {
        using var t = new TestWorld();
        var added = new Recorder(EventMask.Of<Ping>());
        Entity target = t.SpawnWith(OnTick(0, self => self.AddComponent(added)));
        t.SpawnWith(SendEvery.Of(target: target, payloadAt: tick => new Ping { Value = (int)tick }));

        t.Run(4);

        // The entity mask changes right after the run that adds the component, before the routing of that tick.
        added.Pings().Should().Equal((1L, 0), (2L, 1), (3L, 2));
    }

    [Fact]
    public void AMessageSentOnTheTickAComponentWasRemovedNoLongerReachesIt()
    {
        using var t = new TestWorld();
        var removed = new Recorder(EventMask.Of<Ping>());
        Entity target = t.SpawnWith(OnTick(2, self => self.RemoveComponent(removed)), removed);
        t.SpawnWith(SendEvery.Of(target: target, payloadAt: tick => new Ping { Value = (int)tick }));

        t.Run(5);

        // Sent on ticks 0 and 1, it is handled on ticks 1 and 2. Sent on tick 2, it would be handled on tick 3, after the removal.
        removed.Pings().Should().Equal((1L, 0), (2L, 1));
    }

    [Fact]
    public void ARemovedComponentIsGoneFromTheEntity()
    {
        using var t = new TestWorld();
        var removed = new Recorder(EventMask.Of<Ping>());
        Entity e = t.SpawnWith(OnTick(1, self => self.RemoveComponent(removed)), removed);

        t.Run(3);

        t.World.GetComponent<Recorder>(e).Should().BeNull();
        t.World.GetComponents(e).Should().ContainSingle();
    }

    [Fact]
    public void AddingAComponentTheEntityAlreadyHasIsReportedAndChangesNothing()
    {
        using var t = new TestWorld();
        Lambda? component = null;
        component = OnTick(0, self => self.AddComponent(component!));
        Entity e = t.SpawnWith(component);

        t.World.Tick();

        t.World.Errors.Should().ContainSingle().Which.Should().Contain("AddComponent");
        t.World.GetComponents(e).Should().ContainSingle();
    }

    [Fact]
    public void RemovingAComponentTheEntityDoesNotHaveIsLoggedAndChangesNothing()
    {
        using var t = new TestWorld();
        var log = new List<string>();
        t.World.Log += log.Add;
        var stranger = new Recorder();
        Entity e = t.SpawnWith(OnTick(0, self => self.RemoveComponent(stranger)));

        t.World.Tick();

        log.Should().ContainSingle().Which.Should().Contain("RemoveComponent: the entity has no such Recorder");
        t.World.GetComponents(e).Should().ContainSingle();
    }

    // ---- Adding and removing outside a tick ----------------------------------------------------------------------------

    [Fact]
    public void AComponentAddedOutsideATickTakesPartFromTheNextTick()
    {
        using var t = new TestWorld();
        Entity e = t.SpawnWith();
        var recorder = new Recorder(EventMask.Of<Tick>());

        t.World.AddComponent(e, recorder);
        t.World.Tick();

        recorder.Runs.Should().Be(1);
    }

    [Fact]
    public void AComponentRemovedOutsideATickStopsRunning()
    {
        using var t = new TestWorld();
        var recorder = new Recorder(EventMask.Of<Tick>());
        Entity e = t.SpawnWith(recorder);
        t.World.Tick();

        t.World.RemoveComponent(e, recorder).Should().BeTrue();
        t.World.Tick();

        recorder.Runs.Should().Be(1);
    }

    [Fact]
    public void RemovingAComponentTheEntityNoLongerHasReturnsFalse()
    {
        using var t = new TestWorld();
        var recorder = new Recorder();
        Entity e = t.SpawnWith(recorder);
        t.World.RemoveComponent(e, recorder);

        t.World.RemoveComponent(e, recorder).Should().BeFalse();
    }

    [Fact]
    public void AComponentCannotBeAddedToTheSameEntityTwice()
    {
        using var t = new TestWorld();
        var recorder = new Recorder();
        Entity e = t.SpawnWith(recorder);

        Action addAgain = () => t.World.AddComponent(e, recorder);

        addAgain.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AnAddedComponentTakesItsPlaceByPriorityAfterTheOnesOfEqualPriority()
    {
        using var t = new TestWorld();
        var order = new List<string>();

        Lambda Named(string name, int priority) =>
            new(EventMask.Of<Tick>(), (self, scoped ref events, scoped ref outbox) => order.Add(name), priority);

        Entity e = t.SpawnWith(Named("a0", 0), Named("b10", 10));

        t.World.AddComponent(e, Named("c5", 5));
        t.World.AddComponent(e, Named("d-1", -1));
        t.World.AddComponent(e, Named("e5", 5));
        t.World.Tick();

        order.Should().Equal("d-1", "a0", "c5", "e5", "b10");
        t.World.GetComponents(e).Select(component => component.Priority).Should().Equal(-1, 0, 5, 5, 10);
    }

    [Fact]
    public void ADuplicateComponentInstanceInOneSpawnIsRejected()
    {
        using var t = new TestWorld();
        var shared = new Recorder();

        Action spawn = () => t.World.Spawn(new() { Frame = t.Root, Components = [shared, shared] });

        spawn.Should().Throw<ArgumentException>();
    }

    // ---- A component that fails ----------------------------------------------------------------------------------------

    [Fact]
    public void AComponentThatThrowsDoesNotStopTheComponentsAfterItOrTheOtherEntities()
    {
        using var t = new TestWorld();
        Lambda thrower = Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            throw new InvalidOperationException("kaboom"));
        var later = new Recorder(EventMask.Of<Tick>(), priority: 1);
        Entity bad = t.SpawnWith(thrower, later);
        var bystander = new Recorder(EventMask.Of<Tick>());
        t.SpawnWith(bystander);

        t.Run(2);

        later.Runs.Should().Be(2);
        bystander.Runs.Should().Be(2);
        t.World.IsAlive(bad).Should().BeTrue();
    }

    [Fact]
    public void AComponentExceptionIsReportedOncePerRunWithTheEntityTheTypeTheMessageAndTheTick()
    {
        using var t = new TestWorld();
        var logged = new List<string>();
        t.World.Log += logged.Add;
        Entity bad = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            throw new InvalidOperationException("kaboom")));

        t.Run(2);

        t.World.Errors.Should().HaveCount(2);
        t.World.Errors[0].Should().Contain(bad.ToString()).And.Contain(typeof(Lambda).FullName!).And.Contain("kaboom")
            .And.Contain("tick 0");
        t.World.Errors[1].Should().Contain("tick 1");
        logged.Count(line => line.Contains("kaboom")).Should().Be(2);
    }

    [Fact]
    public void SelfLogReachesTheWorldLogWithTheEntityAndTheTick()
    {
        using var t = new TestWorld();
        var log = new List<string>();
        t.World.Log += log.Add;
        Entity e = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            self.Log($"hello {self.Tick}")));

        t.Run(3);

        log.Should().HaveCount(3);
        log[0].Should().Contain("hello 0").And.Contain(e.ToString());
        log[2].Should().Contain("hello 2");
        t.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheErrorListKeepsOnlyTheNewestUpToMaxErrorsAndCanBeCleared()
    {
        using var t = new TestWorld(configure: options => options.MaxErrors = 3);
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            throw new InvalidOperationException($"failure {self.Tick}")));

        t.Run(5);

        t.World.Errors.Should().HaveCount(3);
        t.World.Errors[0].Should().Contain("failure 2");
        t.World.Errors[2].Should().Contain("failure 4");
        t.World.ClearErrors();
        t.World.Errors.Should().BeEmpty();
    }

    // ---- A component that declares its wants wrongly ---------------------------------------------------------------------

    [Fact]
    public void AComponentThatThrowsFromWantsFailsTheSpawnCallOutsideATick()
    {
        using var t = new TestWorld();

        Action spawn = () => t.SpawnWith(new BrokenWants());

        spawn.Should().Throw<NotSupportedException>();
        t.World.Stats.LiveEntities.Should().Be(1, "only the owner of the root frame is live");
    }

    [Fact]
    public void AComponentThatThrowsFromWantsFailsOnlyTheSpawnInsideATickNotTheTick()
    {
        using var t = new TestWorld();
        t.SpawnWith(OnTick(0, self => self.Spawn(new() { Frame = self.Frame, Components = [new BrokenWants()] })));

        t.Run(2);

        t.World.Errors.Should().ContainSingle().Which.Should().Contain(nameof(NotSupportedException));
        t.World.Stats.LiveEntities.Should().Be(2, "the owner of the root frame and the entity that tried to spawn");
    }
}
