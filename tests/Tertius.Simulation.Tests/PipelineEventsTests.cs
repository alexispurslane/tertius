using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The list of events that the components of one entity share during one run. A component edits it in place, removes
/// events from it and adds events to it, and the components after it see the result. It belongs to the run: nothing in
/// it leaves the entity or outlives the run.
/// </summary>
public class PipelineEventsTests
{
    private static Lambda AddsNote(int value, int priority = 0) =>
        new(EventMask.Of<Tick>(),
            (self, scoped ref events, scoped ref outbox) => events.Add(new Note { Value = value }, self.Entity),
            priority);

    // ---- An event that a component adds -----------------------------------------------------------------------------

    [Fact]
    public void AnEventAComponentAddsIsNotSeenByTheComponentsBeforeIt()
    {
        using var t = new TestWorld();
        var before = new Recorder(EventMask.Of<Tick>().And<Note>(), priority: -1);
        t.SpawnWith(before, AddsNote(42));

        t.World.Tick();

        before.History.Should().ContainSingle().Which.EventTypes.Should().Equal(typeof(Tick));
    }

    [Fact]
    public void AComponentBeforeTheOneThatAddsAnEventNeverRunsForThatType()
    {
        using var t = new TestWorld();
        var before = new Recorder(EventMask.Of<Note>(), priority: -1);
        t.SpawnWith(before, AddsNote(42));

        t.Run(3);

        before.Runs.Should().Be(0);
    }

    [Fact]
    public void AnEventAComponentAddsIsSeenByTheComponentsAfterItEvenIfTheyWantNothingElse()
    {
        using var t = new TestWorld();
        var after = new Recorder(EventMask.Of<Note>(), priority: 1);
        Entity e = t.SpawnWith(AddsNote(42), after);

        t.World.Tick();

        // The component wants only Notes, and runs because the list has one by now. It also sees the Tick before it.
        after.History.Should().ContainSingle().Which.EventTypes.Should().Equal(typeof(Tick), typeof(Note));
        Event added = after.Seen[1].Event;
        added.As<Note>().Value.Should().Be(42);
        added.Target.TryGetValue(out Entity target).Should().BeTrue();
        target.Should().Be(e);
        added.Sender.Should().Be(e);
    }

    [Fact]
    public void AnEventAComponentAddsLivesOnlyForTheRun()
    {
        using var t = new TestWorld();
        var after = new Recorder(EventMask.Of<Note>(), priority: 1);
        t.SpawnWith(new Lambda(EventMask.Of<Tick>(), (self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                events.Add(new Note { Value = 1 }, self.Entity);
        }), after);

        t.Run(3);

        after.Runs.Should().Be(1);
    }

    [Fact]
    public void AnEventAComponentAddsIsNotSentToAnyOtherEntity()
    {
        using var t = new TestWorld();
        var other = new Recorder(EventMask.Of<Note>());
        t.SpawnWith(other);
        t.SpawnWith(AddsNote(42));

        t.Run(3);

        other.Seen.Should().BeEmpty();
    }

    // ---- An event that a component removes or edits ---------------------------------------------------------------

    [Fact]
    public void AnEventAComponentRemovesIsNotSeenByTheComponentsAfterIt()
    {
        using var t = new TestWorld();
        var before = new Recorder(EventMask.Of<Ping>(), priority: -1);
        var remover = new Lambda(EventMask.Of<Ping>(),
            (self, scoped ref events, scoped ref outbox) => events.Of<Ping>().Remove());
        var after = new Recorder(EventMask.Of<Ping>(), priority: 1);
        Entity e = t.SpawnWith(before, remover, after);
        t.World.Post(Event.Create(e, Entity.Null, new Ping { Value = 1 }));

        t.Run(2);

        before.PingValues().Should().Equal(1);
        // Nothing is left in the list, so the later component is not even called.
        after.Runs.Should().Be(0);
    }

    [Fact]
    public void APayloadAComponentEditsInPlaceIsSeenEditedByTheComponentsAfterIt()
    {
        using var t = new TestWorld();
        var before = new Recorder(EventMask.Of<Ping>(), priority: -1);
        var doubler = new Lambda(EventMask.Of<Ping>(), (self, scoped ref events, scoped ref outbox) =>
        {
            foreach (ref Ping ping in events.Of<Ping>())
                ping.Value *= 2;
        });
        var after = new Recorder(EventMask.Of<Ping>(), priority: 1);
        Entity e = t.SpawnWith(before, doubler, after);
        t.World.Post(Event.Create(e, Entity.Null, new Ping { Value = 21 }));

        t.Run(2);

        before.PingValues().Should().Equal(21);
        after.PingValues().Should().Equal(42);
    }

    // ---- Each entity has a list of its own ---------------------------------------------------------------------------

    [Fact]
    public void APayloadEditedByOneRecipientOfABroadcastIsUnchangedForTheOthers()
    {
        using var t = new TestWorld();
        var doubler = new Lambda(EventMask.Of<Ping>(), (self, scoped ref events, scoped ref outbox) =>
        {
            foreach (ref Ping ping in events.Of<Ping>())
                ping.Value *= 2;
        });
        var editedCopy = new Recorder(EventMask.Of<Ping>(), priority: 1);
        var untouchedCopy = new Recorder(EventMask.Of<Ping>());
        t.SpawnWith(doubler, editedCopy);
        t.SpawnWith(untouchedCopy);
        t.SpawnWith(SendOnce.Of(new Broadcast(), new Ping { Value = 10 }));

        t.Run(2);

        // The router stores the event once, and each recipient's worker copies it, so an edit stays with the editor.
        editedCopy.PingValues().Should().Equal(20);
        untouchedCopy.PingValues().Should().Equal(10);
    }

    [Fact]
    public void ARemovalFromTheListOfOneEntityDoesNotTakeTheEventAwayFromAnother()
    {
        using var t = new TestWorld();
        var remover = new Lambda(EventMask.Of<Ping>(),
            (self, scoped ref events, scoped ref outbox) => events.Of<Ping>().Remove());
        var keeper = new Recorder(EventMask.Of<Ping>());
        t.SpawnWith(remover);
        t.SpawnWith(keeper);
        t.SpawnWith(SendOnce.Of(new Broadcast(), new Ping { Value = 2 }));

        t.Run(2);

        remover.Runs.Should().Be(1);
        keeper.PingValues().Should().Equal(2);
    }
}
