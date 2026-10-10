namespace Tertius.Simulation.Tests;

public class EventListTests
{
    private static readonly Entity Owner = new(7, 3);
    private static readonly Entity Other = new(9, 1);

    [Fact]
    public void AddFillsTheEnvelopeAndTheMask()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage { Amount = 5, Kind = 2 }, Other);

        Assert.Equal(1, list.Count);
        Assert.True(list[0].Target.TryGetValue(out Entity target));
        Assert.Equal(Owner, target);
        Assert.Equal(Other, list[0].Sender);
        Assert.True(list[0].Is<Damage>());
        Assert.Equal(5, list[0].As<Damage>().Amount);
        Assert.True(list.Mask.Contains<Damage>());
        Assert.False(list.Mask.Contains<Heal>());

        var e = Event.Create<Heal>(new Topic(3), Other, new Heal { Amount = 1 });
        list.Add(e);
        Assert.Equal(2, list.Count);
        Assert.True(list[1].Target.TryGetValue(out Topic topic));
        Assert.Equal(3, topic.Id);
        Assert.True(list.Mask.Contains<Heal>());
    }

    [Fact]
    public void OfEditsPayloadsInPlaceAndSkipsOtherTypes()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage { Amount = 10 });
        list.Add(new Heal { Amount = 3 });
        list.Add(new Damage { Amount = 20 });

        foreach (ref Damage d in list.Of<Damage>())
            d.Amount -= 4;

        Assert.Equal(6, list[0].As<Damage>().Amount);
        Assert.Equal(3, list[1].As<Heal>().Amount);
        Assert.Equal(16, list[2].As<Damage>().Amount);
        Assert.Equal(2, list.Of<Damage>().Count());
        Assert.Equal(0, list.Of<Ping>().Count());
    }

    [Fact]
    public void RemoveAllByPayloadKeepsTheOrderOfTheRest()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        for (int i = 0; i < 6; i++)
        {
            list.Add(new Damage { Amount = i % 2 == 0 ? 0 : i });
            list.Add(new Note { Value = i });
        }

        int removed = list.RemoveAll(static (ref Damage d) => d.Amount == 0);

        Assert.Equal(3, removed);
        Assert.Equal(9, list.Count);
        var order = new List<string>();
        for (int i = 0; i < list.Count; i++)
            order.Add(list[i].Is<Damage>() ? $"d{list[i].As<Damage>().Amount}" : $"n{list[i].As<Note>().Value}");
        Assert.Equal(["n0", "d1", "n1", "n2", "d3", "n3", "n4", "d5", "n5"], order);
        Assert.Equal(0, list.RemoveAll(static (ref Heal _) => true));
    }

    [Fact]
    public void RemoveAllByEvent()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage { Amount = 1 }, Other);
        list.Add(new Damage { Amount = 2 });
        list.Add(new Heal { Amount = 3 }, Other);

        int removed = list.RemoveAll(static (ref Event e) => e.Sender == new Entity(9, 1));

        Assert.Equal(2, removed);
        Assert.Equal(1, list.Count);
        Assert.Equal(2, list[0].As<Damage>().Amount);
    }

    [Fact]
    public void RemoveAtKeepsOrder()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        for (int i = 0; i < 5; i++)
            list.Add(new Note { Value = i });

        list.RemoveAt(1);
        list.RemoveAt(3);
        list.RemoveAt(0);

        Assert.Equal(2, list.Count);
        Assert.Equal(2, list[0].As<Note>().Value);
        Assert.Equal(3, list[1].As<Note>().Value);
    }

    [Fact]
    public void ContainsIndexOfAndCount()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        Assert.False(list.Contains<Damage>());
        list.Add(new Note { Value = 1 });
        list.Add(new Damage());
        list.Add(new Damage());
        Assert.True(list.Contains<Damage>());
        Assert.Equal(1, list.IndexOf<Damage>());
        Assert.Equal(2, list.IndexOf<Damage>(2));
        Assert.Equal(-1, list.IndexOf<Damage>(3));
        Assert.Equal(2, list.CountOf<Damage>());
        Assert.Equal(-1, list.IndexOf<Heal>());
    }

    [Fact]
    public void MaskTracksAddsAndRemovals()
    {
        var buffer = new EventBuffer();
        EventList list = buffer.Begin(Owner);
        list.Add(new Damage());
        list.Add(new Heal());
        list.Add(new Damage());
        Assert.True(list.Mask.Contains<Heal>());

        list.RemoveAll(static (ref Heal _) => true);
        Assert.False(list.Mask.Contains<Heal>());
        Assert.True(list.Mask.Contains<Damage>());

        // One Damage remains, so the mask still names it.
        list.RemoveAt(0);
        Assert.True(list.Mask.Contains<Damage>());
        list.RemoveAt(0);
        Assert.True(list.Mask.IsEmpty);

        // A predicate that removes nothing leaves the mask alone.
        list.Add(new Note());
        Assert.Equal(0, list.RemoveAll(static (ref Event _) => false));
        Assert.True(list.Mask.Contains<Note>());

        // A new list starts with an empty mask.
        list = buffer.Begin(Owner);
        Assert.True(list.Mask.IsEmpty);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void ChangingTheListDuringEnumerationThrows()
    {
        var buffer = new EventBuffer();
        Assert.Throws<InvalidOperationException>(() =>
        {
            EventList list = buffer.Begin(Owner);
            list.Add(new Damage { Amount = 1 });
            list.Add(new Damage { Amount = 2 });
            foreach (ref Damage d in list.Of<Damage>())
                list.Add(new Damage { Amount = d.Amount });
        });
    }

    [Fact]
    public void IndexerRejectsOutOfRange()
    {
        var buffer = new EventBuffer();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            EventList list = buffer.Begin(Owner);
            list.Add(new Note());
            _ = list[1];
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            EventList list = buffer.Begin(Owner);
            list.RemoveAt(0);
        });
    }

    [Fact]
    public void GrowsBeyondItsInitialCapacity()
    {
        var buffer = new EventBuffer(4);
        EventList list = buffer.Begin(Owner);
        for (int i = 0; i < 100; i++)
            list.Add(new Note { Value = i });
        Assert.Equal(100, list.Count);
        int expected = 0;
        foreach (ref Note n in list.Of<Note>())
            Assert.Equal(expected++, n.Value);
        Assert.True(buffer.Capacity >= 100);
    }

    [Fact]
    public void APredicateThatThrowsLeavesAConsistentList()
    {
        var buffer = new EventBuffer();
        int seen = 0;
        int count = -1;
        Assert.Throws<InvalidOperationException>(() =>
        {
            EventList list = buffer.Begin(Owner);
            for (int i = 0; i < 6; i++)
                list.Add(new Note { Value = i });
            try
            {
                list.RemoveAll((ref Note n) =>
                {
                    if (++seen == 4)
                        throw new InvalidOperationException("boom");
                    return n.Value % 2 == 0;
                });
            }
            finally
            {
                count = list.Count;
            }
        });
        // Notes 0 and 2 were removed before the predicate threw on note 3. Notes 1, 3, 4 and 5 remain.
        Assert.Equal(4, count);
    }

    [Fact]
    public void SteadyStateUseDoesNotAllocate()
    {
        var buffer = new EventBuffer(8);
        Exercise(buffer);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            Exercise(buffer);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);

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
