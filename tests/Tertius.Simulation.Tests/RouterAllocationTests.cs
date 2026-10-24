using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The sync point allocates nothing after warm-up, except the bookkeeping of a <c>Parallel.For</c> call when a piece of
/// it runs on the workers (ARCHITECTURE.md, "Threads and the tick"). A routing on the sync thread stays inside that rule.
/// </summary>
public sealed class RouterAllocationTests
{
    private static readonly Topic Squad = Topics.Get("tests.routerallocation.squad");

    [Fact]
    public void ARoutingOnTheSyncThreadAllocatesNothingAfterWarmUp()
    {
        var h = new RouterHarness();
        var everyone = new List<Entity>();
        for (int i = 0; i < 32; i++)
        {
            Entity e = h.Spawn(EventMask.Of<Ping>());
            if (i % 4 == 0)
                h.Subscribe(e, Squad);
            everyone.Add(e);
        }

        // The same direct, topic and delayed sends every tick, so after the warm-up every list and table has its size.
        void SendAndRoute(long tick)
        {
            for (int s = 0; s < 200; s++)
                h.Send(s % everyone.Count, everyone[s * 7 % everyone.Count], new Ping { Value = s });
            h.Send(0, Squad, new Ping { Value = -1 });
            h.SendAt(chunk: 1, due: tick + 2, target: everyone[3], payload: new Ping { Value = -2 });
            h.Router.Route(tick);
        }

        long next = 0;
        for (; next < 50; next++)
            SendAndRoute(next);
        long allocated = Allocations.Fewest(() =>
        {
            for (long end = next + 200; next < end; next++)
                SendAndRoute(next);
        });

        h.Router.DeliveryCount.Should()
            .BeGreaterThan(200, "the routing must have delivered the sends of the last tick");
        allocated.Should().Be(0);
    }
}
