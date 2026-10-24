using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The router on its own, over a hand-built entity table (see <see cref="RouterHarness"/>): in what order the sources
/// of a routing join, how the stable counting sort builds the inboxes, and how long a message waits in a timer. Who
/// receives a message is in <see cref="RouterDeliveryTests"/> and <see cref="RouterTopicTests"/>.
/// </summary>
public class RouterTests
{
    /// <summary>The harness that every test of this class routes in. A subclass runs the same tests on the workers.</summary>
    private protected virtual RouterHarness NewHarness() => new();

    // ---- The order of the sources -------------------------------------------------------------------------------------

    [Fact]
    public void AnInboxListsEngineEventsThenOutboxMessagesThenDueDelayedSendsThenPostedEvents()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());

        // Added in the opposite order of the routing order on purpose.
        h.Router.Post(Event.Create(target: a, sender: Entity.Null, payload: new Ping { Value = 4 }));
        h.SendAt(chunk: 0, due: 5, target: a, payload: new Ping { Value = 3 });
        h.Send(0, a, new Ping { Value = 2 });
        h.Router.AddEngineEvent(Event.Create(target: a, sender: Entity.Null, payload: new Ping { Value = 1 }));

        h.Route(tick: 5);

        h.Pings(a).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void AnInboxListsTheChunkOutboxesInChunkOrderWhateverOrderTheyWereFilledIn()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());

        h.Send(2, a, new Ping { Value = 30 });
        h.Send(0, a, new Ping { Value = 10 });
        h.Send(1, a, new Ping { Value = 20 });
        h.Send(0, a, new Ping { Value = 11 });

        h.Route(tick: 0);

        // Chunk 0 first, and inside a chunk the order of the sends.
        h.Pings(a).Should().Equal(10, 11, 20, 30);
    }

    [Fact]
    public void DelayedSendsDueTogetherAndSentOnTheSameTickKeepChunkOrderThenSendOrder()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());

        h.SendAt(chunk: 1, due: 5, target: a, payload: new Ping { Value = 31 });
        h.SendAt(chunk: 0, due: 5, target: a, payload: new Ping { Value = 30 });
        h.SendAt(chunk: 0, due: 5, target: a, payload: new Ping { Value = 32 });

        h.Route(tick: 5);

        h.Pings(a).Should().Equal(30, 32, 31);
    }

    [Fact]
    public void ADelayedSendThatWasQueuedEarlierStaysAheadOfOneQueuedLaterWhateverTheirChunks()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());

        // Chunk 1 sends first and chunk 0 sends one routing later, and both are due at routing 5.
        h.SendAt(chunk: 1, due: 5, target: a, payload: new Ping { Value = 1 });
        h.Route(tick: 3);
        h.SendAt(chunk: 0, due: 5, target: a, payload: new Ping { Value = 2 });
        h.Route(tick: 4);
        h.Route(tick: 5);

        h.Pings(a).Should().Equal(1, 2);
    }

    [Fact]
    public void ADelayedSendWaitsForItsDueRoutingAndJoinsOnlyThatOne()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        h.SendAt(chunk: 0, due: 6, target: a, payload: new Ping { Value = 1 });

        h.Route(tick: 5);
        h.Pings(a).Should().BeEmpty();
        h.Router.RoutedCount.Should().Be(0);

        h.Route(tick: 6);
        h.Pings(a).Should().Equal(1);
        h.Router.RoutedCount.Should().Be(1);

        h.Route(tick: 7);
        h.Pings(a).Should().BeEmpty();
        h.Router.RoutedCount.Should().Be(0);
    }

    [Fact]
    public void DelayedSendsJoinTheRoutingOfTheirOwnDueTickWhateverOrderTheyWereSentIn()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());

        // The due ticks alternate, so that no two neighbours in the chunk's list share a bucket.
        h.SendAt(chunk: 0, due: 8, target: a, payload: new Ping { Value = 80 });
        h.SendAt(chunk: 0, due: 6, target: a, payload: new Ping { Value = 61 });
        h.SendAt(chunk: 0, due: 7, target: a, payload: new Ping { Value = 70 });
        h.SendAt(chunk: 0, due: 6, target: a, payload: new Ping { Value = 62 });

        h.Route(tick: 6);
        h.Pings(a).Should().Equal(61, 62);
        h.Route(tick: 7);
        h.Pings(a).Should().Equal(70);
        h.Route(tick: 8);
        h.Pings(a).Should().Equal(80);
    }

    [Fact]
    public void ATimerThatHasFiredIsReusedWithoutReplayingItsMessages()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        h.SendAt(chunk: 0, due: 1, target: a, payload: new Ping { Value = 1 });
        h.Route(tick: 1);

        // A later send reaches the list that the first one used.
        h.SendAt(chunk: 0, due: 2, target: a, payload: new Ping { Value = 2 });
        h.Route(tick: 2);

        h.Pings(a).Should().Equal(2);
    }

    [Fact]
    public void AMessageIsRoutedOnceWhicheverSourceItCameFrom()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        h.Router.AddEngineEvent(Event.Create(target: a, sender: Entity.Null, payload: new Ping { Value = 1 }));
        h.Send(0, a, new Ping { Value = 2 });
        h.SendAt(chunk: 0, due: 1, target: a, payload: new Ping { Value = 3 });
        h.Router.Post(Event.Create(target: a, sender: Entity.Null, payload: new Ping { Value = 4 }));
        h.Route(tick: 1);
        h.Pings(a).Should().Equal(1, 2, 3, 4);

        h.Route(tick: 2);

        h.Pings(a).Should().BeEmpty();
        h.Router.RoutedCount.Should().Be(0);
    }

    // ---- The inboxes --------------------------------------------------------------------------------------------------

    [Fact]
    public void AnEventIsStoredOnceHoweverManyRecipientsItHas()
    {
        RouterHarness h = NewHarness();
        for (int i = 0; i < 10; i++)
            h.Spawn(EventMask.Of<Ping>());
        h.Send(0, new Broadcast(), new Ping { Value = 1 });

        h.Route(tick: 0);

        h.Router.RoutedCount.Should().Be(1);
        h.Router.DeliveryCount.Should().Be(10);
    }

    [Fact]
    public void AnInboxStaysInRoutingOrderWhenTheRecipientsOfNeighbouringEventsInterleave()
    {
        RouterHarness h = NewHarness();
        var everyone = new List<Entity>();
        for (int i = 0; i < 10; i++)
            everyone.Add(h.Spawn(EventMask.Of<Ping>()));
        Entity first = everyone[0], quiet = everyone[4], last = everyone[^1];
        h.Router.Post(Event.Create(target: last, sender: Entity.Null, payload: new Ping { Value = 1 }));

        // Each broadcast touches every slot, and the direct sends jump between the two ends.
        h.Send(0, last, new Ping { Value = 100 });
        h.Send(0, new Broadcast(), new Ping { Value = 200 });
        h.Send(0, first, new Ping { Value = 300 });
        h.Send(0, last, new Ping { Value = 400 });
        h.Send(0, new Broadcast(), new Ping { Value = 500 });

        h.Route(tick: 0);

        h.Pings(first).Should().Equal(200, 300, 500);
        h.Pings(last).Should().Equal(100, 200, 400, 500, 1);
        h.Pings(quiet).Should().Equal(200, 500);
        // Six events: five sends and one post. The deliveries: four single ones and two broadcasts to ten entities each.
        h.Router.RoutedCount.Should().Be(6);
        h.Router.DeliveryCount.Should().Be(24);
    }

    [Fact]
    public void InboxesStayCompleteAndInOrderWhenEventsAndDeliveriesOutgrowTheStartingBuffers()
    {
        RouterHarness h = NewHarness();
        Entity[] entities =
            [h.Spawn(EventMask.Of<Ping>()), h.Spawn(EventMask.Of<Ping>()), h.Spawn(EventMask.Of<Ping>())];

        // 300 direct sends, value i to entity i mod 3, then 50 broadcasts: 350 events and 300 + 150 deliveries.
        for (int i = 0; i < 300; i++)
            h.Send(0, entities[i % 3], new Ping { Value = i });
        for (int i = 0; i < 50; i++)
            h.Send(0, new Broadcast(), new Ping { Value = 1000 + i });

        h.Route(tick: 0);

        for (int k = 0; k < 3; k++)
        {
            // Entity k got k, k + 3, ... k + 297, and then the 50 broadcasts.
            int[] expected = [.. Enumerable.Range(0, 100).Select(j => k + 3 * j), .. Enumerable.Range(1000, 50)];
            h.Pings(entities[k]).Should().Equal(expected);
        }

        h.Router.RoutedCount.Should().Be(350);
        h.Router.DeliveryCount.Should().Be(450);
    }

    [Fact]
    public void ARoutingReplacesTheInboxesOfTheRoutingBefore()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        Entity b = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, a, new Ping { Value = 1 });
        h.Route(tick: 0);
        h.Pings(a).Should().Equal(1);

        h.Send(0, b, new Ping { Value = 2 });
        h.Route(tick: 1);

        h.Pings(a).Should().BeEmpty();
        h.Pings(b).Should().Equal(2);
        h.Router.RoutedCount.Should().Be(1);
        h.Router.DeliveryCount.Should().Be(1);
    }

    [Fact]
    public void AnEntityThatTakesAReleasedSlotStartsWithAnEmptyInbox()
    {
        RouterHarness h = NewHarness();
        Entity old = h.Spawn(EventMask.Of<Ping>());
        Entity other = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, old, new Ping { Value = 1 });
        h.Send(0, other, new Ping { Value = 2 });
        h.Route(tick: 0);
        h.Router.InboxCount(old).Should().Be(1);

        // Released between two routings, as an outside-tick Destroy does, and taken again at once.
        h.Entities.Release(old);
        Entity reused = h.Spawn(EventMask.Of<Ping>());

        reused.Index.Should().Be(old.Index);
        h.Inbox(reused).Should().BeEmpty();
        h.Pings(other).Should().Equal(2);
        h.Router.AssertInvariants();
    }

    [Fact]
    public void AnEntityThatIsLiveForTheFirstTimeAfterTheLastRoutingHasAnEmptyInbox()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, a, new Ping { Value = 1 });
        h.Route(tick: 0);

        // The slot did not exist when the routing ran.
        Entity fresh = h.Spawn(EventMask.Of<Ping>());

        h.Router.InboxCount(fresh).Should().Be(0);
        h.Inbox(fresh).Should().BeEmpty();
    }
}
