using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Who the router delivers a message to, for the three ways to address one (an entity, a topic, everyone): the mask
/// filter, the rule that a Destroy always gets through, and what happens when the recipient is gone, or arrives, before
/// the routing.
/// </summary>
public class RouterDeliveryTests
{
    /// <summary>The harness that every test of this class routes in. A subclass runs the same tests on the workers.</summary>
    private protected virtual RouterHarness NewHarness() => new();

    private static readonly Topic Squad = Topics.Get("tests.routerdelivery.squad");
    private static readonly Topic Convoy = Topics.Get("tests.routerdelivery.convoy");

    // ---- The three ways to address a message -----------------------------------------------------------------------

    [Fact]
    public void ADirectMessageReachesOnlyItsTarget()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        Entity b = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, b, new Ping { Value = 1 });

        h.Route(tick: 0);

        h.Pings(a).Should().BeEmpty();
        h.Pings(b).Should().Equal(1);
    }

    [Fact]
    public void ATopicMessageReachesEverySubscriberOnceAndNobodyElse()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        Entity b = h.Spawn(EventMask.Of<Ping>());
        Entity outsider = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(a, Squad);
        h.Subscribe(b, Squad);
        // A second subscription to the same topic changes nothing.
        h.Topics.Subscribe(b, Squad);
        h.Send(0, Squad, new Ping { Value = 7 });

        h.Route(tick: 0);

        h.Pings(a).Should().Equal(7);
        h.Pings(b).Should().Equal(7);
        h.Pings(outsider).Should().BeEmpty();
        h.Router.DeliveryCount.Should().Be(2);
    }

    [Fact]
    public void AnEntityInTwoTopicsHearsEachMessageOnceInRoutingOrder()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(a, Squad);
        h.Subscribe(a, Convoy);
        h.Send(0, Squad, new Ping { Value = 1 });
        h.Send(0, Convoy, new Ping { Value = 2 });
        h.Send(0, Squad, new Ping { Value = 3 });

        h.Route(tick: 0);

        h.Pings(a).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void ABroadcastReachesEveryLiveEntity()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        Entity b = h.Spawn(EventMask.Of<Ping>());
        Entity c = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, new Broadcast(), new Ping { Value = 9 });

        h.Route(tick: 0);

        h.Pings(a).Should().Equal(9);
        h.Pings(b).Should().Equal(9);
        h.Pings(c).Should().Equal(9);
    }

    // ---- The mask filter ------------------------------------------------------------------------------------------

    [Fact]
    public void ADirectMessageIsDroppedWhenItsTargetDoesNotWantItsType()
    {
        RouterHarness h = NewHarness();
        Entity pings = h.Spawn(EventMask.Of<Ping>());
        Entity heals = h.Spawn(EventMask.Of<Heal>());
        h.Send(0, heals, new Ping { Value = 1 });
        h.Send(0, pings, new Ping { Value = 2 });

        h.Route(tick: 0);

        h.Inbox(heals).Should().BeEmpty();
        h.Pings(pings).Should().Equal(2);
        h.Router.DeliveryCount.Should().Be(1);
    }

    [Fact]
    public void ATopicMessageSkipsSubscribersThatDoNotWantItsType()
    {
        RouterHarness h = NewHarness();
        Entity pings = h.Spawn(EventMask.Of<Ping>());
        Entity heals = h.Spawn(EventMask.Of<Heal>());
        h.Subscribe(pings, Squad);
        h.Subscribe(heals, Squad);
        h.Send(0, Squad, new Ping { Value = 2 });

        h.Route(tick: 0);

        h.Pings(pings).Should().Equal(2);
        h.Inbox(heals).Should().BeEmpty();
        h.Router.DeliveryCount.Should().Be(1);
    }

    [Fact]
    public void ABroadcastSkipsEntitiesThatDoNotWantItsType()
    {
        RouterHarness h = NewHarness();
        Entity pings = h.Spawn(EventMask.Of<Ping>());
        Entity heals = h.Spawn(EventMask.Of<Heal>());
        Entity both = h.Spawn(EventMask.Of<Ping>().And<Heal>());
        Entity none = h.Spawn(EventMask.None);
        h.Send(0, new Broadcast(), new Heal { Amount = 3 });

        h.Route(tick: 0);

        h.Inbox(pings).Should().BeEmpty();
        h.Heals(heals).Should().Equal(3f);
        h.Heals(both).Should().Equal(3f);
        h.Inbox(none).Should().BeEmpty();
        h.Router.DeliveryCount.Should().Be(2);
    }

    [Fact]
    public void AnEntityThatWantsTwoTypesGetsBothInRoutingOrder()
    {
        RouterHarness h = NewHarness();
        Entity both = h.Spawn(EventMask.Of<Ping>().And<Heal>());
        h.Send(0, both, new Ping { Value = 1 });
        h.Send(0, both, new Heal { Amount = 2 });
        h.Send(0, both, new Ping { Value = 3 });

        h.Route(tick: 0);

        string[] inbox = [.. h.Inbox(both).Select(e => e.Is<Ping>() ? $"ping {e.As<Ping>().Value}" : $"heal {e.As<Heal>().Amount}")];
        inbox.Should().Equal("ping 1", "heal 2", "ping 3");
    }

    [Fact]
    public void AnEventNobodyWantsIsRoutedButWritesNoDelivery()
    {
        RouterHarness h = NewHarness();
        Entity heals = h.Spawn(EventMask.Of<Heal>());
        h.Send(0, heals, new Ping { Value = 1 });

        h.Route(tick: 0);

        h.Router.RoutedCount.Should().Be(1);
        h.Router.DeliveryCount.Should().Be(0);
    }

    // ---- Destroy always gets through ---------------------------------------------------------------------------------

    [Fact]
    public void ADirectDestroyReachesAnEntityThatWantsNothing()
    {
        RouterHarness h = NewHarness();
        Entity deaf = h.Spawn(EventMask.None);
        h.Send(0, deaf, new Destroy());

        h.Route(tick: 0);

        h.Destroys(deaf).Should().Be(1);
    }

    [Fact]
    public void ATopicDestroyReachesEverySubscriberWhateverTheyWant()
    {
        RouterHarness h = NewHarness();
        Entity deaf = h.Spawn(EventMask.None);
        Entity heals = h.Spawn(EventMask.Of<Heal>());
        h.Subscribe(deaf, Squad);
        h.Subscribe(heals, Squad);
        h.Send(0, Squad, new Destroy());

        h.Route(tick: 0);

        h.Destroys(deaf).Should().Be(1);
        h.Destroys(heals).Should().Be(1);
    }

    [Fact]
    public void ABroadcastDestroyReachesEveryLiveEntityWhateverTheyWant()
    {
        RouterHarness h = NewHarness();
        Entity deaf = h.Spawn(EventMask.None);
        Entity heals = h.Spawn(EventMask.Of<Heal>());
        h.Send(0, new Broadcast(), new Destroy());

        h.Route(tick: 0);

        h.Destroys(deaf).Should().Be(1);
        h.Destroys(heals).Should().Be(1);
    }

    // ---- Recipients that are gone, or new, at the routing ------------------------------------------------------------

    [Fact]
    public void ADirectMessageForAStaleHandleReachesNeitherItsOldNorItsNewOwner()
    {
        RouterHarness h = NewHarness();
        Entity doomed = h.Spawn(EventMask.Of<Ping>());
        h.Entities.Release(doomed);
        Entity reused = h.Spawn(EventMask.Of<Ping>());
        reused.Index.Should().Be(doomed.Index);
        h.Send(0, doomed, new Ping { Value = 1 });

        h.Route(tick: 0);

        h.Inbox(reused).Should().BeEmpty();
        h.Router.DeliveryCount.Should().Be(0);
    }

    [Fact]
    public void AMessageToTheNullEntityReachesNobody()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, Entity.Null, new Ping { Value = 1 });

        h.Route(tick: 0);

        h.Pings(a).Should().BeEmpty();
        h.Router.DeliveryCount.Should().Be(0);
    }

    [Fact]
    public void ADestroyForAStaleHandleDoesNotReachTheEntityThatReusedItsSlot()
    {
        RouterHarness h = NewHarness();
        Entity doomed = h.Spawn(EventMask.None);
        h.Entities.Release(doomed);
        Entity reused = h.Spawn(EventMask.None);
        reused.Index.Should().Be(doomed.Index);
        h.Send(0, doomed, new Destroy());

        h.Route(tick: 0);

        h.Destroys(reused).Should().Be(0);
    }

    [Fact]
    public void ADelayedMessageForAnEntityThatWasReleasedBeforeItWasDueIsDropped()
    {
        RouterHarness h = NewHarness();
        Entity doomed = h.Spawn(EventMask.Of<Ping>());
        h.SendAt(0, 3, doomed, new Ping { Value = 1 });
        h.Route(tick: 2);
        h.Entities.Release(doomed);
        Entity reused = h.Spawn(EventMask.Of<Ping>());
        reused.Index.Should().Be(doomed.Index);

        h.Route(tick: 3);

        h.Inbox(reused).Should().BeEmpty();
        h.Router.DeliveryCount.Should().Be(0);
    }

    [Fact]
    public void ABroadcastSkipsEntitiesThatWereReleased()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        Entity gone = h.Spawn(EventMask.Of<Ping>());
        Entity c = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, new Broadcast(), new Ping { Value = 7 });
        h.Entities.Release(gone);

        h.Route(tick: 0);

        h.Pings(a).Should().Equal(7);
        h.Pings(c).Should().Equal(7);
        h.Router.DeliveryCount.Should().Be(2);
    }

    [Fact]
    public void ABroadcastReachesTheEntityThatTookTheSlotOfAReleasedOne()
    {
        RouterHarness h = NewHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        Entity gone = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, new Broadcast(), new Ping { Value = 7 });
        h.Entities.Release(gone);
        Entity heir = h.Spawn(EventMask.Of<Ping>());
        heir.Index.Should().Be(gone.Index);

        h.Route(tick: 0);

        h.Pings(a).Should().Equal(7);
        h.Pings(heir).Should().Equal(7);
    }

    [Fact]
    public void ABroadcastReachesAnEntityThatWentLiveAfterItWasSentButBeforeItWasRouted()
    {
        RouterHarness h = NewHarness();
        Entity early = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, new Broadcast(), new Ping { Value = 7 });
        Entity late = h.Spawn(EventMask.Of<Ping>());

        h.Route(tick: 0);

        h.Pings(early).Should().Equal(7);
        h.Pings(late).Should().Equal(7);
    }

    [Fact]
    public void ATopicMessageReachesWhoeverSubscribedBeforeItWasRouted()
    {
        RouterHarness h = NewHarness();
        Entity early = h.Spawn(EventMask.Of<Ping>());
        Entity late = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(early, Squad);
        h.Send(0, Squad, new Ping { Value = 1 });
        h.Subscribe(late, Squad);

        h.Route(tick: 0);

        h.Pings(early).Should().Equal(1);
        h.Pings(late).Should().Equal(1);
    }

    [Fact]
    public void ATopicMessageSkipsWhoeverUnsubscribedBeforeItWasRouted()
    {
        RouterHarness h = NewHarness();
        Entity stays = h.Spawn(EventMask.Of<Ping>());
        Entity leaves = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(stays, Squad);
        h.Subscribe(leaves, Squad);
        h.Send(0, Squad, new Ping { Value = 1 });
        h.Topics.Unsubscribe(leaves, Squad).Should().BeTrue();

        h.Route(tick: 0);

        h.Pings(stays).Should().Equal(1);
        h.Inbox(leaves).Should().BeEmpty();
    }
}
