using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// When an entity runs and which of its components get called, in what order. How the components change the list of
/// events they share is in <see cref="PipelineEventsTests"/>, and how an entity's components change, and fail, is in
/// <see cref="PipelineComponentTests"/>.
/// </summary>
public class PipelineTests
{
    private static Lambda Named(List<string> log, string name, int priority, EventMask wants) =>
        new(wants, (self, scoped ref events, scoped ref outbox) => log.Add(name), priority);

    // ---- The order of the components ------------------------------------------------------------------------------

    [Fact]
    public void ComponentsRunFromTheLowestPriorityToTheHighestWhateverOrderTheyWereGiven()
    {
        using var t = new TestWorld();
        var log = new List<string>();
        EventMask tick = EventMask.Of<Tick>();
        t.SpawnWith(Named(log: log, name: "3", priority: 3, wants: tick), Named(log: log, name: "-2", priority: -2, wants: tick), Named(log: log, name: "1", priority: 1, wants: tick),
            Named(log: log, name: "0", priority: 0, wants: tick));

        t.World.Tick();

        log.Should().Equal("-2", "0", "1", "3");
    }

    [Fact]
    public void ComponentsOfEqualPriorityRunInTheOrderTheyWereGivenEvenWhenOthersStandBetweenThem()
    {
        using var t = new TestWorld();
        var log = new List<string>();
        EventMask tick = EventMask.Of<Tick>();
        t.SpawnWith(
            Named(log: log, name: "5a", priority: 5, wants: tick),
            Named(log: log, name: "1a", priority: 1, wants: tick),
            Named(log: log, name: "5b", priority: 5, wants: tick),
            Named(log: log, name: "1b", priority: 1, wants: tick),
            Named(log: log, name: "3", priority: 3, wants: tick),
            Named(log: log, name: "-2", priority: -2, wants: tick));

        t.World.Tick();

        log.Should().Equal("-2", "1a", "1b", "3", "5a", "5b");
    }

    [Fact]
    public void TheOrderOfTheComponentsHoldsOnEveryRun()
    {
        using var t = new TestWorld();
        var log = new List<string>();
        EventMask tick = EventMask.Of<Tick>();
        t.SpawnWith(Named(log: log, name: "late", priority: 1, wants: tick), Named(log: log, name: "early", priority: 0, wants: tick));

        t.Run(3);

        log.Should().Equal("early", "late", "early", "late", "early", "late");
    }

    // ---- Which components are called ---------------------------------------------------------------------------------

    [Fact]
    public void AComponentWhoseWantsDoNotOverlapTheListIsSkipped()
    {
        using var t = new TestWorld();
        var pings = new Recorder(EventMask.Of<Ping>());
        var heals = new Recorder(EventMask.Of<Heal>());
        Entity target = t.SpawnWith(pings, heals);
        t.SpawnWith(SendOnce.Of(target: target, payload: new Ping { Value = 1 }));

        t.Run(2);

        pings.Runs.Should().Be(1);
        heals.Runs.Should().Be(0);
    }

    [Fact]
    public void AComponentThatIsCalledSeesTheWholeListNotOnlyTheTypesItWants()
    {
        using var t = new TestWorld();
        var ticker = new Recorder(EventMask.Of<Tick>());
        var pings = new Recorder(EventMask.Of<Ping>());
        Entity target = t.SpawnWith(ticker, pings);
        t.World.Post(Event.Create(target: target, sender: Entity.Null, payload: new Ping { Value = 1 }));

        t.Run(2);

        // On tick 1 the list holds the Tick and the Ping, and the component that wants only Pings sees both.
        pings.History.Should().ContainSingle().Which.EventTypes.Should().Equal(typeof(Tick), typeof(Ping));
    }

    // ---- When an entity runs ------------------------------------------------------------------------------------------

    [Fact]
    public void AnEntityThatWantsTickRunsOnEveryTickEvenWithNoMail()
    {
        using var t = new TestWorld();
        var ticker = new Recorder(EventMask.Of<Tick>());
        t.SpawnWith(ticker);

        t.Run(5);

        ticker.History.Select(run => run.Tick).Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public void AnEntityThatDoesNotWantTickRunsOnlyOnTheTicksWhenMailArrives()
    {
        using var t = new TestWorld();
        var inbox = new Recorder(EventMask.Of<Ping>());
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(SendEvery.Of(target: target, payload: new Ping { Value = 1 }, every: 3));

        t.Run(9);

        // Sent on ticks 0, 3 and 6, so mail arrives on ticks 1, 4 and 7, and the entity runs on no other tick.
        inbox.History.Select(run => run.Tick).Should().Equal(1, 4, 7);
    }

    [Fact]
    public void AnEntityWithNothingToHandleDoesNotRun()
    {
        using var t = new TestWorld();
        var idle = new Recorder(EventMask.Of<Ping>());
        t.SpawnWith(idle);

        t.Run(5);

        idle.Runs.Should().Be(0);
    }

    [Fact]
    public void AMessageOfATypeNoComponentWantsIsDroppedAndDoesNotWakeTheEntity()
    {
        using var t = new TestWorld();
        var idle = new Recorder(EventMask.Of<Ping>());
        Entity e = t.SpawnWith(idle);

        t.World.Post(Event.Create(target: e, sender: Entity.Null, payload: new Heal()));
        t.Run(2);

        idle.Runs.Should().Be(0);
        t.World.Stats.Deliveries.Should().Be(0);
    }

    [Fact]
    public void AnIdleEntityRunsWhenAMessageItWantsFinallyArrives()
    {
        using var t = new TestWorld();
        var idle = new Recorder(EventMask.Of<Ping>());
        Entity e = t.SpawnWith(idle);
        t.Run(5);

        t.World.Post(Event.Create(target: e, sender: Entity.Null, payload: new Ping { Value = 3 }));
        t.Run(2);

        idle.Pings().Should().Equal((6L, 3));
    }

    // ---- The engine's own events -------------------------------------------------------------------------------------

    [Fact]
    public void TickCarriesTheTickNumberAndTheTimeStep()
    {
        using var t = new TestWorld();
        var ticker = new Recorder(EventMask.Of<Tick>());
        t.SpawnWith(ticker);

        t.Run(3);

        ticker.Seen.Select(seen => seen.Event.As<Tick>().Number).Should().Equal(0, 1, 2);
        ticker.Seen.Select(seen => seen.Event.As<Tick>().DeltaTime).Should().AllBeEquivalentTo(t.World.DeltaTime);
    }

    [Fact]
    public void TickComesFirstThenSpawnedThenTheInbox()
    {
        using var t = new TestWorld();
        var recorder = new Recorder(EventMask.Of<Tick>().And<Spawned>().And<Ping>());
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 0)
                return;
            // The child is spawned and sent a message on the same tick, so its first run has all three kinds of event.
            Entity child = self.Spawn(new() { Frame = self.Frame, Components = [recorder] });
            outbox.Send(child, new Ping { Value = 1 });
        }));

        t.Run(2);

        recorder.History.Should().ContainSingle().Which.EventTypes.Should()
            .Equal(typeof(Tick), typeof(Spawned), typeof(Ping));
    }

    [Fact]
    public void SpawnedIsDeliveredOnceToAnEntityThatWantsItAndNeverToOneThatDoesNot()
    {
        using var t = new TestWorld();
        var wants = new Recorder(EventMask.Of<Spawned>());
        var ignores = new Recorder(EventMask.Of<Ping>());
        t.SpawnWith(wants);
        t.SpawnWith(ignores);

        t.Run(3);

        wants.Runs.Should().Be(1);
        wants.Seen.Single().Event.Is<Spawned>().Should().BeTrue();
        ignores.Runs.Should().Be(0);
    }
}
