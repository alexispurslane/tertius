using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// How the router resolves a topic: a frame topic names a frame by index and generation, so a message to a frame that has
/// died, or to an older frame at an index that a new frame now uses, reaches nobody.
/// </summary>
public class RouterTopicTests
{
    private static readonly Topic Squad = Topics.Get("tests.routertopic.squad");

    [Fact]
    public void AFrameTopicReachesTheMembersOfThatFrameAndNobodyInAnotherFrame()
    {
        var h = new RouterHarness();
        FrameHandle bay = RouterHarness.Frame(index: 0, generation: 1);
        FrameHandle hangar = RouterHarness.Frame(index: 1, generation: 1);
        h.Topics.AddFrame(bay);
        h.Topics.AddFrame(hangar);
        Entity inBay = h.Spawn(EventMask.Of<Ping>(), bay);
        Entity inHangar = h.Spawn(EventMask.Of<Ping>(), hangar);
        h.Send(0, bay.Topic, new Ping { Value = 7 });

        h.Route(tick: 0);

        h.Pings(inBay).Should().Equal(7);
        h.Pings(inHangar).Should().BeEmpty();
    }

    [Fact]
    public void AFrameTopicReachesMembersAndSubscribersOnceInSlotOrder()
    {
        var h = new RouterHarness();
        FrameHandle bay = RouterHarness.Frame(index: 0, generation: 1);
        h.Topics.AddFrame(bay);
        Entity outsider = h.Spawn(EventMask.Of<Ping>());
        Entity member = h.Spawn(EventMask.Of<Ping>(), bay);
        Entity memberAndSubscriber = h.Spawn(EventMask.Of<Ping>(), bay);
        h.Subscribe(memberAndSubscriber, bay.Topic);
        h.Subscribe(outsider, bay.Topic);

        h.RecipientsOf(bay.Topic).Should().Equal(outsider, member, memberAndSubscriber);

        h.Send(0, bay.Topic, new Ping { Value = 7 });
        h.Route(tick: 0);
        h.Pings(outsider).Should().Equal(7);
        h.Pings(member).Should().Equal(7);
        h.Pings(memberAndSubscriber).Should().Equal(7);
        h.Router.DeliveryCount.Should().Be(3);
    }

    [Fact]
    public void AMessageToTheTopicOfADeadFrameReachesNobody()
    {
        var h = new RouterHarness();
        FrameHandle bay = RouterHarness.Frame(index: 3, generation: 1);
        h.Topics.AddFrame(bay);
        Entity inside = h.Spawn(EventMask.Of<Ping>(), bay);
        Entity watcher = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(watcher, bay.Topic);
        h.Topics.MoveMember(inside, FrameHandle.Null, out _);
        h.Topics.RemoveFrame(bay);
        h.Send(0, bay.Topic, new Ping { Value = 1 });

        h.Route(tick: 0);

        h.Pings(inside).Should().BeEmpty();
        h.Pings(watcher).Should().BeEmpty();
        h.Router.DeliveryCount.Should().Be(0);
    }

    [Fact]
    public void AMessageToAnOlderGenerationOfAFrameIndexDoesNotReachTheFrameThatReusesTheIndex()
    {
        var h = new RouterHarness();
        FrameHandle first = RouterHarness.Frame(index: 3, generation: 1);
        h.Topics.AddFrame(first);
        Entity inside = h.Spawn(EventMask.Of<Ping>(), first);
        h.Topics.MoveMember(inside, FrameHandle.Null, out _);
        h.Topics.RemoveFrame(first);
        FrameHandle second = RouterHarness.Frame(index: 3, generation: 2);
        h.Topics.AddFrame(second);
        h.Topics.MoveMember(inside, second, out _);
        second.Topic.Id.Should().Be(first.Topic.Id);

        h.Send(0, first.Topic, new Ping { Value = 2 });
        h.Send(0, second.Topic, new Ping { Value = 4 });
        h.Route(tick: 0);

        h.Pings(inside).Should().Equal(4);
    }

    [Fact]
    public void AFrameTopicWithoutAGenerationReachesNobody()
    {
        var h = new RouterHarness();
        FrameHandle bay = RouterHarness.Frame(index: 2, generation: 1);
        h.Topics.AddFrame(bay);
        Entity inside = h.Spawn(EventMask.Of<Ping>(), bay);
        h.Send(0, new Topic(bay.Topic.Id), new Ping { Value = 1 });

        h.Route(tick: 0);

        h.Pings(inside).Should().BeEmpty();
        h.Router.DeliveryCount.Should().Be(0);
    }

    [Fact]
    public void ASubscriberToAFramesTopicDoesNotHearTheNextFrameAtItsIndex()
    {
        var h = new RouterHarness();
        FrameHandle first = RouterHarness.Frame(index: 3, generation: 1);
        h.Topics.AddFrame(first);
        Entity watcher = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(watcher, first.Topic);
        h.Topics.RemoveFrame(first);
        FrameHandle second = RouterHarness.Frame(index: 3, generation: 2);
        h.Topics.AddFrame(second);

        h.Send(0, second.Topic, new Ping { Value = 1 });
        h.Route(tick: 0);

        h.Pings(watcher).Should().BeEmpty();
        h.Topics.Subscribe(watcher, first.Topic).Should().BeFalse();
    }

    [Fact]
    public void ANewFrameAtAReusedIndexStartsWithNoMembersOrSubscribers()
    {
        var h = new RouterHarness();
        FrameHandle first = RouterHarness.Frame(index: 2, generation: 1);
        h.Topics.AddFrame(first);
        Entity inside = h.Spawn(EventMask.Of<Ping>(), first);
        Entity watcher = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(watcher, first.Topic);
        h.Topics.MembersOf(first).ToArray().Should().Equal(inside);

        // The frame is forgotten while its sets still hold entities: the sets empty at once.
        h.Topics.RemoveFrame(first);
        FrameHandle second = RouterHarness.Frame(index: 2, generation: 2);
        h.Topics.AddFrame(second);

        h.Topics.MembersOf(second).ToArray().Should().BeEmpty();
        h.Topics.MembersOf(first).ToArray().Should().BeEmpty();
        h.RecipientsOf(second.Topic).Should().BeEmpty();
        // The old frame's subscriber lost its subscription, so it can subscribe to the new frame.
        h.Topics.Subscribe(watcher, second.Topic).Should().BeTrue();
    }

    [Fact]
    public void TopicsThatNoSetBacksReachNobody()
    {
        var h = new RouterHarness();
        Entity listener = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(listener, Squad);

        // Named topics past every subscribed id, and frame topics at and far past the known frame indices.
        h.Send(0, new Topic(Squad.Id + 1), new Ping { Value = 1 });
        h.Send(0, new Topic(Squad.Id + 1000), new Ping { Value = 2 });
        h.Send(0, new Topic(-1 - 16, 1), new Ping { Value = 3 });
        h.Send(0, new Topic(-1 - 1000, 1), new Ping { Value = 4 });
        h.Route(tick: 0);

        h.Pings(listener).Should().BeEmpty();
        h.Router.RoutedCount.Should().Be(4);
        h.Router.DeliveryCount.Should().Be(0);
        h.Topics.MembersOf(RouterHarness.Frame(index: 16, generation: 1)).ToArray().Should().BeEmpty();
        h.Topics.MembersOf(RouterHarness.Frame(index: 1000, generation: 1)).ToArray().Should().BeEmpty();
        h.Topics.IsLive(RouterHarness.Frame(index: 16, generation: 1)).Should().BeFalse();
        h.Topics.IsLive(FrameHandle.Null).Should().BeFalse();
    }

    [Fact]
    public void NamedTopicsCanBeSubscribedInAnyIdOrder()
    {
        var h = new RouterHarness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        int[] ids = [Squad.Id + 3, Squad.Id + 4, Squad.Id + 1, Squad.Id];
        foreach (int id in ids)
            h.Subscribe(a, new Topic(id));
        foreach (int id in ids)
            h.Send(0, new Topic(id), new Ping { Value = id });

        h.Route(tick: 0);

        h.Pings(a).Should().Equal(ids);
    }
}
