using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Queries over an inbox: <see cref="EventList.Of{T}"/> picks a payload type, <c>From</c> picks a sender, <c>Where</c>
/// tests the payload, and the endings <c>foreach</c>, <c>Count</c>, <c>Any</c>, <c>TryFirst</c> and <c>Remove</c> run
/// the query. A stored <see cref="EventQuery{T}"/> runs through <see cref="EventList.Find{T}(EventQuery{T})"/> with the
/// same words. No query allocates.
/// </summary>
public sealed class EventQueryTests
{
    private static readonly Entity Owner = new(7, 3);
    private static readonly Entity Alice = new(9, 1);
    private static readonly Entity Bob = new(11, 2);

    // Damage 1 from Alice, a Heal from Alice, damage 2 from Bob, damage 3 from Alice, damage 4 with no sender.
    private static EventList MixedInbox(EventBuffer buffer)
    {
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage { Amount = 1 }, Alice);
        list.Add(new Heal { Amount = 10 }, Alice);
        list.Add(new Damage { Amount = 2 }, Bob);
        list.Add(new Damage { Amount = 3 }, Alice);
        list.Add(new Damage { Amount = 4 });
        return list;
    }

    private static List<float> Amounts(EventList list)
    {
        var amounts = new List<float>();
        foreach (ref Damage d in list.Of<Damage>())
            amounts.Add(d.Amount);
        return amounts;
    }

    // ---- Endings ------------------------------------------------------------------------------------------------------

    [Fact]
    public void AnyIsTrueWhenAnEventMatchesAndFalseWhenNoneDoes()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().Any().Should().BeTrue();
        list.Of<Note>().Any().Should().BeFalse();
        list.Of<Damage>().Where(static (ref d) => d.Amount > 100).Any().Should().BeFalse();
    }

    [Fact]
    public void TryFirstCopiesThePayloadOfTheFirstMatchInInboxOrder()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().Where(static (ref d) => d.Amount > 1).TryFirst(out Damage first).Should().BeTrue();

        first.Amount.Should().Be(2);
    }

    [Fact]
    public void TryFirstWithoutAMatchReturnsFalseAndADefaultPayload()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().Where(static (ref d) => d.Amount > 100).TryFirst(out Damage first).Should().BeFalse();

        first.Should().Be(default(Damage));
    }

    [Fact]
    public void RemoveRemovesTheMatchesKeepsTheOrderOfTheRestAndReturnsTheirNumber()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        for (int i = 0; i < 6; i++)
        {
            list.Add(new Damage { Amount = i % 2 == 0 ? 0 : i });
            list.Add(new Note { Value = i });
        }

        int removed = list.Of<Damage>().Where(static (ref d) => d.Amount == 0).Remove();

        removed.Should().Be(3);
        var order = new List<string>();
        for (int i = 0; i < list.Count; i++)
            order.Add(list[i].Is<Damage>() ? $"d{list[i].As<Damage>().Amount}" : $"n{list[i].As<Note>().Value}");
        order.Should().Equal("n0", "d1", "n1", "n2", "d3", "n3", "n4", "d5", "n5");
    }

    [Fact]
    public void RemoveWithoutAMatchRemovesNothing()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().Where(static (ref d) => d.Amount > 100).Remove().Should().Be(0);

        list.Count.Should().Be(5);
    }

    [Fact]
    public void RemoveWithoutWordsRemovesEveryEventOfTheType()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().Remove().Should().Be(4);

        list.Count.Should().Be(1);
        list[0].Is<Heal>().Should().BeTrue();
    }

    [Fact]
    public void ForeachOverANarrowedQueryVisitsOnlyTheMatchesInInboxOrderAndEditsThemInPlace()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        foreach (ref Damage d in list.Of<Damage>().From(Alice))
            d.Amount *= 10;

        Amounts(list).Should().Equal(10, 2, 30, 4);
    }

    // ---- From ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void FromKeepsOnlyTheEventsOfOneSender()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().From(Alice).Count().Should().Be(2);
        list.Of<Damage>().From(Bob).Count().Should().Be(1);
        list.Of<Heal>().From(Bob).Any().Should().BeFalse();
    }

    [Fact]
    public void FromTheNullEntityKeepsTheEventsThatHaveNoSender()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().From(Entity.Null).TryFirst(out Damage unsent).Should().BeTrue();

        unsent.Amount.Should().Be(4);
        list.Of<Damage>().From(Entity.Null).Count().Should().Be(1);
    }

    // ---- Where --------------------------------------------------------------------------------------------------------

    [Fact]
    public void WhereKeepsOnlyThePayloadsThatPassThePredicate()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().Where(static (ref d) => d.Amount >= 3).Count().Should().Be(2);
    }

    [Fact]
    public void WhereWithStatePassesTheStateToThePredicate()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().Where(2f, static (ref d, limit) => d.Amount > limit).Count().Should().Be(2);
        list.Of<Damage>().Where(3f, static (ref d, limit) => d.Amount > limit).Count().Should().Be(1);
    }

    [Fact]
    public void FromAndWhereTogetherKeepOnlyTheEventsThatPassBoth()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().From(Alice).Where(static (ref d) => d.Amount > 1).Count().Should().Be(1);
        list.Of<Damage>().Where(1f, static (ref d, limit) => d.Amount > limit).From(Alice).Count().Should().Be(1);
        list.Of<Damage>().From(Alice).Where(1f, static (ref d, limit) => d.Amount > limit).Count().Should().Be(1);
    }

    [Fact]
    public void ThePredicateRunsOnlyOnEventsOfTheQueriedTypeAndSender()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);
        var seen = new List<float>();

        list.Of<Damage>().From(Alice).Where(seen, static (ref d, seen) =>
        {
            seen.Add(d.Amount);
            return false;
        }).Count();

        seen.Should().Equal(1, 3);
    }

    [Fact]
    public void ThePredicateCanEditThePayloadInPlace()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Of<Damage>().Where(static (ref d) =>
        {
            d.Kind = 5;
            return true;
        }).Count();

        list[0].As<Damage>().Kind.Should().Be(5);
    }

    // ---- One sender and one predicate ---------------------------------------------------------------------------------

    [Fact]
    public void AQueryHoldsOnePredicateSoASecondWhereThrows()
    {
        var buffer = new EventBuffer();
        Action twoPlain = () =>
            buffer.Begin(Owner).Of<Damage>().Where(static (ref _) => true).Where(static (ref _) => true);
        Action plainThenState = () =>
            buffer.Begin(Owner).Of<Damage>().Where(static (ref _) => true).Where(1, static (ref _, _) => true);
        Action stored = () => new EventQuery<Damage>().Where(static (ref _) => true).Where(static (ref _) => true);
        Action anyType = () => buffer.Begin(Owner).Where(static (ref _) => true).Where(static (ref _) => true);

        twoPlain.Should().Throw<InvalidOperationException>();
        plainThenState.Should().Throw<InvalidOperationException>();
        stored.Should().Throw<InvalidOperationException>();
        anyType.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AQueryHoldsOneSenderSoASecondFromThrows()
    {
        var buffer = new EventBuffer();
        Action typed = () => buffer.Begin(Owner).Of<Damage>().From(Alice).From(Bob);
        Action withState = () =>
            buffer.Begin(Owner).Of<Damage>().From(Alice).Where(1, static (ref _, _) => true).From(Bob);
        Action stored = () => new EventQuery<Damage>().From(Alice).From(Alice);
        Action anyType = () => buffer.Begin(Owner).From(Alice).From(Bob);

        typed.Should().Throw<InvalidOperationException>();
        withState.Should().Throw<InvalidOperationException>();
        stored.Should().Throw<InvalidOperationException>();
        anyType.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WhereRejectsANullPredicate()
    {
        var buffer = new EventBuffer();
        Action plain = () => buffer.Begin(Owner).Of<Damage>().Where(null!);
        Action withState = () => buffer.Begin(Owner).Of<Damage>().Where(1, null!);
        Action stored = () => new EventQuery<Damage>().Where(null!);
        Action storedWithState = () => new EventQuery<Damage>().Where(1, null!);
        Action anyType = () => buffer.Begin(Owner).Where(null!);

        plain.Should().Throw<ArgumentNullException>();
        withState.Should().Throw<ArgumentNullException>();
        stored.Should().Throw<ArgumentNullException>();
        storedWithState.Should().Throw<ArgumentNullException>();
        anyType.Should().Throw<ArgumentNullException>();
    }

    // ---- Stored queries -----------------------------------------------------------------------------------------------

    [Fact]
    public void FindRunsAStoredQueryLikeTheChainWithTheSameWords()
    {
        EventQuery<Damage> query = new EventQuery<Damage>().From(Alice).Where(static (ref d) => d.Amount > 1);
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Find(query).Count().Should()
            .Be(list.Of<Damage>().From(Alice).Where(static (ref d) => d.Amount > 1).Count());
        list.Find(query).TryFirst(out Damage first).Should().BeTrue();
        first.Amount.Should().Be(3);
    }

    [Fact]
    public void TheDefaultStoredQueryMatchesEveryEventOfItsType()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Find(default(EventQuery<Damage>)).Count().Should().Be(4);
        list.Find(new EventQuery<Heal>()).Count().Should().Be(1);
    }

    [Fact]
    public void TheDefaultStoredQueryWithStateHasNoPredicateAndMatchesEveryEventOfItsType()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Find(default(EventQuery<Damage, int>)).Count().Should().Be(4);
    }

    [Fact]
    public void AWordLeavesTheStoredQueryItStartedFromUnchanged()
    {
        var everyDamage = new EventQuery<Damage>();
        EventQuery<Damage> fromAlice = everyDamage.From(Alice);
        _ = everyDamage.Where(static (ref _) => false);
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Find(everyDamage).Count().Should().Be(4);
        list.Find(fromAlice).Count().Should().Be(2);
    }

    [Fact]
    public void AStoredQueryWithStateRunsThroughFind()
    {
        EventQuery<Damage, float> query = new EventQuery<Damage>().Where(2f, static (ref d, limit) => d.Amount > limit)
            .From(Alice);
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        list.Find(query).Count().Should().Be(1);
        list.Find(query).Any().Should().BeTrue();
        list.Find(query).TryFirst(out Damage first).Should().BeTrue();
        first.Amount.Should().Be(3);
    }

    [Fact]
    public void AStoredQueryRunsOnEachNewInbox()
    {
        EventQuery<Damage> query = new EventQuery<Damage>().From(Bob);
        var buffer = new EventBuffer();

        MixedInbox(buffer).Find(query).Count().Should().Be(1);
        EventList second = buffer.Begin(Owner);
        second.Add(new Damage(), Bob);
        second.Add(new Damage(), Bob);
        second.Find(query).Count().Should().Be(2);
    }

    // ---- Every ending of the state form -------------------------------------------------------------------------------

    [Fact]
    public void AQueryWithStateHasEveryEnding()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);
        var visited = new List<float>();

        foreach (ref Damage d in list.Of<Damage>().Where(2f, static (ref d, limit) => d.Amount >= limit))
        {
            visited.Add(d.Amount);
            d.Kind = 1;
        }

        bool any = list.Of<Damage>().Where(4f, static (ref d, limit) => d.Amount >= limit).Any();
        bool none = list.Of<Damage>().Where(5f, static (ref d, limit) => d.Amount >= limit).Any();
        list.Of<Damage>().Where(3f, static (ref d, limit) => d.Amount >= limit).TryFirst(out Damage first).Should()
            .BeTrue();
        int removed = list.Of<Damage>().Where(3f, static (ref d, limit) => d.Amount >= limit).Remove();

        visited.Should().Equal(2, 3, 4);
        list[2].As<Damage>().Kind.Should().Be(1, "the enumerator yields references into the inbox");
        any.Should().BeTrue();
        none.Should().BeFalse();
        first.Amount.Should().Be(3);
        removed.Should().Be(2);
        Amounts(list).Should().Equal(1, 2);
    }

    // ---- The event mask ------------------------------------------------------------------------------------------------

    [Fact]
    public void AQueryForATypeThatTheMaskLacksDoesNotScanTheInbox()
    {
        // The mask does not see a change of Type through the indexer. So this event holds a Heal that the mask does not
        // name: a scan would find it, and the early exit on the mask is what keeps every ending from seeing it.
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage());
        list[0].Type = EventType<Heal>.Id;
        list.Where(static (ref e) => e.Is<Heal>()).Count().Should().Be(1, "a scan finds the Heal");
        int visited = 0;

        foreach (ref Heal _ in list.Of<Heal>())
            visited++;
        foreach (ref Heal _ in list.Of<Heal>().Where(1, static (ref _, _) => true))
            visited++;

        visited.Should().Be(0);
        list.Of<Heal>().Count().Should().Be(0);
        list.Of<Heal>().Any().Should().BeFalse();
        list.Of<Heal>().TryFirst(out _).Should().BeFalse();
        list.Find(new EventQuery<Heal>().Where(1, static (ref _, _) => true)).Count().Should().Be(0);
        list.Of<Heal>().Remove().Should().Be(0);
        list.Count.Should().Be(1);
    }

    [Fact]
    public void AQueryForATypeTheInboxDoesNotHoldNeverCallsThePredicate()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);
        int[] calls = [0];

        list.Of<Note>().Where(calls, static (ref _, calls) => ++calls[0] > 0).Remove().Should().Be(0);

        calls[0].Should().Be(0);
    }

    // ---- Queries over every type ----------------------------------------------------------------------------------------

    [Fact]
    public void WhereOnTheInboxTestsEveryEventOfEveryType()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        int removed = list.Where(static (ref e) => e.Sender == new Entity(9, 1)).Remove();

        removed.Should().Be(3);
        list.Count.Should().Be(2);
        Amounts(list).Should().Equal(2, 4);
    }

    [Fact]
    public void FromOnTheInboxKeepsTheEventsOfEveryTypeFromOneSender()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);
        var types = new List<string>();

        foreach (ref Event e in list.From(Alice))
            types.Add(e.Is<Damage>() ? $"d{e.As<Damage>().Amount}" : "heal");

        types.Should().Equal("d1", "heal", "d3");
        list.From(Alice).Count().Should().Be(3);
        list.From(Entity.Null).Count().Should().Be(1);
    }

    [Fact]
    public void AQueryOverEveryTypeHasEveryEnding()
    {
        var buffer = new EventBuffer();
        EventList list = MixedInbox(buffer);

        foreach (ref Event e in list.From(Bob))
            e.As<Damage>().Kind = 8;
        bool any = list.From(Alice).Where(static (ref e) => e.Is<Heal>()).Any();
        bool none = list.From(Bob).Where(static (ref e) => e.Is<Heal>()).Any();
        list.Where(static (ref e) => e.Is<Heal>()).TryFirst(out Event heal).Should().BeTrue();
        list.Where(static (ref e) => e.Is<Note>()).TryFirst(out Event missing).Should().BeFalse();
        int removed = list.Where(static (ref e) => e.Is<Damage>()).From(Alice).Remove();

        list[1].As<Damage>().Kind.Should().Be(8, "the enumerator yields references into the inbox");
        any.Should().BeTrue();
        none.Should().BeFalse();
        heal.As<Heal>().Amount.Should().Be(10);
        missing.Type.Should().Be(default(Event).Type);
        missing.Sender.Should().Be(Entity.Null);
        removed.Should().Be(2);
        Amounts(list).Should().Equal(2, 4);
    }

    // ---- Changes while enumerating -------------------------------------------------------------------------------------

    [Fact]
    public void ChangingTheListWhileEnumeratingAnyQueryThrows()
    {
        var buffer = new EventBuffer();
        Action narrowed = () =>
        {
            EventList list = MixedInbox(buffer);
            foreach (ref Damage _ in list.Of<Damage>().From(Alice))
                list.Add(new Note());
        };
        Action withState = () =>
        {
            EventList list = MixedInbox(buffer);
            foreach (ref Damage _ in list.Of<Damage>().Where(0f, static (ref d, limit) => d.Amount > limit))
                list.Of<Damage>().Remove();
        };
        Action anyType = () =>
        {
            EventList list = MixedInbox(buffer);
            foreach (ref Event _ in list.From(Alice))
                list.RemoveAt(0);
        };

        narrowed.Should().Throw<InvalidOperationException>();
        withState.Should().Throw<InvalidOperationException>();
        anyType.Should().Throw<InvalidOperationException>();
    }

    // ---- Allocation ----------------------------------------------------------------------------------------------------

    [Fact]
    public void NoQueryAllocates()
    {
        var buffer = new EventBuffer(16);
        EventQuery<Damage> stored = new EventQuery<Damage>().From(Alice).Where(static (ref d) => d.Amount > 0);
        EventQuery<Damage, float> storedWithState =
            new EventQuery<Damage>().Where(1f, static (ref d, limit) => d.Amount > limit);
        Exercise(buffer, stored, storedWithState);
        long allocated = Allocations.Fewest(() =>
        {
            for (int i = 0; i < 100; i++)
                Exercise(buffer, stored, storedWithState);
        });

        allocated.Should().Be(0);

        static void Exercise(EventBuffer buffer, EventQuery<Damage> stored, EventQuery<Damage, float> storedWithState)
        {
            EventList list = MixedInbox(buffer);
            float limit = 1;
            foreach (ref Damage d in list.Of<Damage>().From(Alice).Where(static (ref d) => d.Amount > 0))
                d.Kind++;
            foreach (ref Damage d in list.Of<Damage>().Where(limit, static (ref d, limit) => d.Amount > limit))
                d.Kind++;
            foreach (ref Event e in list.From(Alice))
                e.Sender = Alice;
            _ = list.Of<Damage>().Count() + list.Of<Heal>().From(Bob).Count() + list.Find(stored).Count() +
                list.Find(storedWithState).Count();
            _ = list.Of<Note>().Any() || list.Find(stored).Any() || list.Where(static (ref e) => e.Is<Note>()).Any();
            _ = list.Of<Damage>().Where(limit, static (ref d, limit) => d.Amount > limit).TryFirst(out Damage _);
            _ = list.From(Bob).TryFirst(out Event _);
            _ = list.Of<Damage>().From(Bob).Remove();
            _ = list.Find(storedWithState).From(Entity.Null).Remove();
            _ = list.Where(static (ref e) => e.Is<Heal>()).Remove();
        }
    }
}
