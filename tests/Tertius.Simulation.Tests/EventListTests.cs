using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The list of events that the components of one entity share during a run, on its own: adding, editing and removing
/// events, the mask that tells the scheduler what the list holds, and the rule that the list does not allocate.
/// </summary>
public class EventListTests
{
    private static readonly Entity Owner = new(7, 3);
    private static readonly Entity Other = new(9, 1);

    // ---- Adding ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void AddingAPayloadMakesAnEventForTheOwnerFromTheGivenSender()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);

        list.Add(new Damage { Amount = 5, Kind = 2 }, Other);

        list.Count.Should().Be(1);
        list[0].Target.TryGetValue(out Entity target).Should().BeTrue();
        target.Should().Be(Owner);
        list[0].Sender.Should().Be(Other);
        list[0].Is<Damage>().Should().BeTrue();
        list[0].As<Damage>().Amount.Should().Be(5);
        list[0].As<Damage>().Kind.Should().Be(2);
    }

    [Fact]
    public void AnEventAddedWithoutASenderHasNone()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);

        list.Add(new Note { Value = 1 });

        list[0].Sender.Should().Be(Entity.Null);
    }

    [Fact]
    public void AddingAnEventKeepsItsOwnTargetAndSender()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);

        list.Add(Event.Create<Heal>(new Topic(3), Other, new Heal { Amount = 1 }));

        list.Count.Should().Be(1);
        list[0].Target.TryGetValue(out Topic topic).Should().BeTrue();
        topic.Id.Should().Be(3);
        list[0].Sender.Should().Be(Other);
    }

    [Fact]
    public void TheMaskNamesTheTypesOfTheEventsAdded()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);

        list.Add(new Damage());
        list.Add(Event.Create(Owner, Other, new Heal()));

        list.Mask.Contains<Damage>().Should().BeTrue();
        list.Mask.Contains<Heal>().Should().BeTrue();
        list.Mask.Contains<Note>().Should().BeFalse();
    }

    [Fact]
    public void BeginClearsTheEventsAndTheMaskOfThePreviousRun()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage());

        list = buffer.Begin(Owner);

        list.Count.Should().Be(0);
        list.Mask.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void TheListKnowsItsOwner()
    {
        var buffer = new EventBuffer();

        EventList list = buffer.Begin(Owner);

        list.Owner.Should().Be(Owner);
    }

    [Fact]
    public void TheListGrowsBeyondItsInitialCapacityAndKeepsEveryEventInOrder()
    {
        var buffer = new EventBuffer(4);
        EventList list = buffer.Begin(Owner);

        for (int i = 0; i < 100; i++)
            list.Add(new Note { Value = i });

        list.Count.Should().Be(100);
        int expected = 0;
        foreach (ref Note n in list.Of<Note>())
            n.Value.Should().Be(expected++);
        expected.Should().Be(100);
        buffer.Capacity.Should().BeGreaterThanOrEqualTo(100);
    }

    // ---- Reading ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void AnEditThroughTheIndexerChangesTheStoredEvent()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage { Amount = 10 });

        list[0].As<Damage>().Amount = 3;

        list[0].As<Damage>().Amount.Should().Be(3);
    }

    [Fact]
    public void AsSpanShowsTheEventsInTheOrderTheyWereAdded()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Note { Value = 1 });
        list.Add(new Heal { Amount = 2 });
        list.Add(new Note { Value = 3 });

        Event[] events = list.AsSpan().ToArray();

        events.Select(e => e.Is<Note>() ? $"note {e.As<Note>().Value}" : $"heal {e.As<Heal>().Amount}")
            .Should().Equal("note 1", "heal 2", "note 3");
    }

    [Fact]
    public void OfYieldsReferencesSoEditsLandInTheListAndOtherTypesAreSkipped()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage { Amount = 10 });
        list.Add(new Heal { Amount = 3 });
        list.Add(new Damage { Amount = 20 });

        foreach (ref Damage d in list.Of<Damage>())
            d.Amount -= 4;

        list[0].As<Damage>().Amount.Should().Be(6);
        list[1].As<Heal>().Amount.Should().Be(3);
        list[2].As<Damage>().Amount.Should().Be(16);
    }

    [Fact]
    public void OfCountsOnlyTheEventsOfItsType()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage());
        list.Add(new Heal());
        list.Add(new Damage());

        list.Of<Damage>().Count().Should().Be(2);
        list.Of<Ping>().Count().Should().Be(0);
    }

    [Fact]
    public void ContainsAndIndexOfFindTheFirstEventOfATypeFromAStartingIndex()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Contains<Damage>().Should().BeFalse();
        list.Add(new Note { Value = 1 });
        list.Add(new Damage());
        list.Add(new Damage());

        list.Contains<Damage>().Should().BeTrue();
        list.IndexOf<Damage>().Should().Be(1);
        list.IndexOf<Damage>(2).Should().Be(2);
        list.IndexOf<Damage>(3).Should().Be(-1);
        list.IndexOf<Heal>().Should().Be(-1);
    }

    [Fact]
    public void IndexOfTreatsANegativeStartAsTheStartOfTheList()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage());

        list.IndexOf<Damage>(-5).Should().Be(0);
    }

    [Fact]
    public void CountOfCountsTheEventsOfOneType()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Note());
        list.Add(new Damage());
        list.Add(new Damage());

        list.CountOf<Damage>().Should().Be(2);
        list.CountOf<Heal>().Should().Be(0);
    }

    // ---- Removing ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void RemoveAllByPayloadKeepsTheOrderOfTheRestAndReturnsTheNumberRemoved()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        for (int i = 0; i < 6; i++)
        {
            list.Add(new Damage { Amount = i % 2 == 0 ? 0 : i });
            list.Add(new Note { Value = i });
        }

        int removed = list.RemoveAll(static (ref Damage d) => d.Amount == 0);

        removed.Should().Be(3);
        list.Count.Should().Be(9);
        var order = new List<string>();
        for (int i = 0; i < list.Count; i++)
            order.Add(list[i].Is<Damage>() ? $"d{list[i].As<Damage>().Amount}" : $"n{list[i].As<Note>().Value}");
        order.Should().Equal("n0", "d1", "n1", "n2", "d3", "n3", "n4", "d5", "n5");
    }

    [Fact]
    public void RemoveAllByPayloadOfATypeInTheListThatNothingMatchesRemovesNothing()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage { Amount = 1 });

        list.RemoveAll(static (ref Damage d) => d.Amount > 100).Should().Be(0);

        list.Count.Should().Be(1);
    }

    [Fact]
    public void RemoveAllByPayloadOfATypeNotInTheListNeverCallsThePredicate()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage());
        int calls = 0;

        int removed = list.RemoveAll((ref Heal _) =>
        {
            calls++;
            return true;
        });

        removed.Should().Be(0);
        calls.Should().Be(0);
    }

    [Fact]
    public void RemoveAllByEventRemovesEveryEventThatMatches()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage { Amount = 1 }, Other);
        list.Add(new Damage { Amount = 2 });
        list.Add(new Heal { Amount = 3 }, Other);

        int removed = list.RemoveAll(static (ref Event e) => e.Sender == new Entity(9, 1));

        removed.Should().Be(2);
        list.Count.Should().Be(1);
        list[0].As<Damage>().Amount.Should().Be(2);
    }

    [Fact]
    public void RemoveAllRejectsANullPredicateOfEitherKind()
    {
        var buffer = new EventBuffer();
        Action byPayload = () =>
        {
            EventList list = buffer.Begin(Owner);
            list.RemoveAll<Damage>(null!);
        };
        Action byEvent = () =>
        {
            EventList list = buffer.Begin(Owner);
            list.RemoveAll((RefPredicate<Event>)null!);
        };

        byPayload.Should().Throw<ArgumentNullException>();
        byEvent.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveAtKeepsTheOrderOfTheOtherEvents()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        for (int i = 0; i < 5; i++)
            list.Add(new Note { Value = i });

        list.RemoveAt(1);
        list.RemoveAt(3);
        list.RemoveAt(0);

        // Removing 1 leaves 0 2 3 4; removing index 3 leaves 0 2 3; removing index 0 leaves 2 3.
        list.Count.Should().Be(2);
        list[0].As<Note>().Value.Should().Be(2);
        list[1].As<Note>().Value.Should().Be(3);
    }

    [Fact]
    public void RemoveAtCanRemoveTheLastEvent()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Note { Value = 1 });
        list.Add(new Note { Value = 2 });

        list.RemoveAt(1);

        list.Count.Should().Be(1);
        list[0].As<Note>().Value.Should().Be(1);
    }

    // ---- The mask ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheMaskDropsATypeWhenRemoveAllRemovesItsLastEvent()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage());
        list.Add(new Heal());
        list.Add(new Damage());

        list.RemoveAll(static (ref Heal _) => true);

        list.Mask.Contains<Heal>().Should().BeFalse();
        list.Mask.Contains<Damage>().Should().BeTrue();
    }

    [Fact]
    public void TheMaskKeepsATypeUntilItsLastEventIsRemoved()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage());
        list.Add(new Damage());

        list.RemoveAt(0);
        list.Mask.Contains<Damage>().Should().BeTrue();
        list.RemoveAt(0);

        list.Mask.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ARemovalThatRemovesNothingLeavesTheMaskAlone()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Note());

        list.RemoveAll(static (ref Event _) => false).Should().Be(0);

        list.Mask.Contains<Note>().Should().BeTrue();
    }

    // ---- Errors ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AddingToTheListWhileEnumeratingItThrows()
    {
        var buffer = new EventBuffer();
        Action act = () =>
        {
            EventList list = buffer.Begin(Owner);
            list.Add(new Damage { Amount = 1 });
            list.Add(new Damage { Amount = 2 });
            foreach (ref Damage d in list.Of<Damage>())
                list.Add(new Damage { Amount = d.Amount });
        };

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RemovingFromTheListWhileEnumeratingItThrows()
    {
        var buffer = new EventBuffer();
        Action removeAt = () =>
        {
            EventList list = buffer.Begin(Owner);
            list.Add(new Damage { Amount = 1 });
            list.Add(new Damage { Amount = 2 });
            foreach (ref Damage _ in list.Of<Damage>())
                list.RemoveAt(0);
        };
        Action removeAll = () =>
        {
            EventList list = buffer.Begin(Owner);
            list.Add(new Damage { Amount = 1 });
            list.Add(new Damage { Amount = 2 });
            foreach (ref Damage _ in list.Of<Damage>())
                list.RemoveAll(static (ref Damage d) => d.Amount == 1);
        };

        removeAt.Should().Throw<InvalidOperationException>();
        removeAll.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TheIndexerRejectsAnIndexPastTheEndOrBelowZero()
    {
        var buffer = new EventBuffer();
        Action pastTheEnd = () =>
        {
            EventList list = buffer.Begin(Owner);
            list.Add(new Note());
            _ = list[1];
        };
        Action belowZero = () =>
        {
            EventList list = buffer.Begin(Owner);
            list.Add(new Note());
            _ = list[-1];
        };

        pastTheEnd.Should().Throw<ArgumentOutOfRangeException>();
        belowZero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RemoveAtRejectsAnIndexOutsideTheList()
    {
        var buffer = new EventBuffer();
        Action fromEmpty = () => buffer.Begin(Owner).RemoveAt(0);
        Action pastTheEnd = () =>
        {
            EventList list = buffer.Begin(Owner);
            list.Add(new Note());
            list.RemoveAt(1);
        };

        fromEmpty.Should().Throw<ArgumentOutOfRangeException>();
        pastTheEnd.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void APredicateThatThrowsKeepsTheEventsFromTheFailureOnUnchanged()
    {
        var buffer = new EventBuffer();
        int calls = 0;
        List<int> remaining = [];
        Action removeAll = () =>
        {
            EventList list = buffer.Begin(Owner);
            for (int i = 0; i < 6; i++)
                list.Add(new Note { Value = i });
            try
            {
                list.RemoveAll((ref Note n) =>
                {
                    if (++calls == 4)
                        throw new InvalidOperationException("boom");
                    return n.Value % 2 == 0;
                });
            }
            finally
            {
                for (int i = 0; i < list.Count; i++)
                    remaining.Add(list[i].As<Note>().Value);
            }
        };

        removeAll.Should().Throw<InvalidOperationException>();

        // Notes 0 and 2 were removed before the predicate threw on note 3, and notes 1, 3, 4 and 5 remain.
        remaining.Should().Equal(1, 3, 4, 5);
    }

    // ---- Allocation -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void SteadyStateUseDoesNotAllocate()
    {
        var buffer = new EventBuffer(8);
        Exercise(buffer);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            Exercise(buffer);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);

        static void Exercise(EventBuffer buffer)
        {
            EventList list = buffer.Begin(Owner);
            for (int i = 0; i < 64; i++)
            {
                if (i % 3 == 0)
                    list.Add(new Heal { Amount = i });
                else
                    list.Add(new Damage { Amount = i });
            }
            foreach (ref Damage d in list.Of<Damage>())
                d.Amount *= 0.5f;
            list.RemoveAll(static (ref Damage d) => d.Amount < 4);
            list.RemoveAt(0);
            _ = list.Contains<Heal>();
        }
    }
}
