using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

public struct Damage : IEventPayload
{
    public float Amount;
    public int Kind;
}

public struct Heal : IEventPayload
{
    public float Amount;
}

public struct Note : IEventPayload
{
    public int Value;
}

/// <summary>
/// Is deferred LINQ as fast as a loop over an event list? Two questions, each with its own baseline:
/// <list type="bullet">
/// <item>Edit: lower every Damage payload in place. LINQ cannot edit in place, so its closest equivalent, a projected
/// copy, is measured separately.</item>
/// <item>Sum: the same read-only reduction, the sum of Damage.Amount, done by every approach.</item>
/// </list>
/// Half of the events are Damage, a quarter Heal and a quarter Note, interleaved.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EventQueryBenchmarks
{
    private static readonly Entity Owner = new(1, 1);

    private EventBuffer buffer = null!;
    private Event[] events = null!;

    [Params(64, 4096)]
    public int Count;

    [GlobalSetup]
    public void Setup()
    {
        EngineEvents.Register();
        EventTypeRegistry.Register<Damage>();
        EventTypeRegistry.Register<Heal>();
        EventTypeRegistry.Register<Note>();
        events = new Event[Count];
        buffer = new EventBuffer(Count);
        EventList list = buffer.Begin(Owner);
        for (int i = 0; i < Count; i++)
        {
            events[i] = (i % 4) switch
            {
                0 or 2 => Event.Create(Owner, Owner, new Damage { Amount = i % 17, Kind = i % 3 }),
                1 => Event.Create(Owner, Owner, new Heal { Amount = 1 }),
                _ => Event.Create(Owner, Owner, new Note { Value = i }),
            };
            list.Add(events[i]);
        }
    }

    // ---- Edit in place ---------------------------------------------------------------------------------------------

    [Benchmark(Description = "EventList.Of<T> foreach (ref)")]
    [BenchmarkCategory("Edit")]
    public void EditEventListOf()
    {
        EventList list = buffer.View(Owner);
        foreach (ref Damage d in list.Of<Damage>())
            d.Amount -= 1f;
    }

    [Benchmark(Baseline = true, Description = "for loop, ref Event")]
    [BenchmarkCategory("Edit")]
    public void EditForLoop()
    {
        Event[] items = events;
        for (int i = 0; i < items.Length; i++)
        {
            ref Event e = ref items[i];
            if (e.Is<Damage>())
                e.As<Damage>().Amount -= 1f;
        }
    }

    [Benchmark(Description = "LINQ Select edited copies .ToArray()")]
    [BenchmarkCategory("Edit")]
    public Event[] EditLinqCopy() =>
        events.Select(static e =>
        {
            if (e.Is<Damage>())
                e.As<Damage>().Amount -= 1f;
            return e;
        }).ToArray();

    // ---- Read-only reduction ---------------------------------------------------------------------------------------

    [Benchmark(Description = "EventList.Of<T> foreach (ref)")]
    [BenchmarkCategory("Sum")]
    public float SumEventListOf()
    {
        float sum = 0;
        EventList list = buffer.View(Owner);
        foreach (ref Damage d in list.Of<Damage>())
            sum += d.Amount;
        return sum;
    }

    [Benchmark(Baseline = true, Description = "for loop, ref Event")]
    [BenchmarkCategory("Sum")]
    public float SumForLoop()
    {
        float sum = 0;
        Event[] items = events;
        for (int i = 0; i < items.Length; i++)
        {
            ref Event e = ref items[i];
            if (e.Is<Damage>())
                sum += e.As<Damage>().Amount;
        }
        return sum;
    }

    [Benchmark(Description = "foreach Event by value")]
    [BenchmarkCategory("Sum")]
    public float SumForeachByValue()
    {
        float sum = 0;
        foreach (Event e in events)
            if (e.TryGet(out Damage d))
                sum += d.Amount;
        return sum;
    }

    [Benchmark(Description = "LINQ Where/Select/Sum")]
    [BenchmarkCategory("Sum")]
    public float SumLinq() =>
        events.Where(static e => e.Is<Damage>()).Select(static e => e.As<Damage>().Amount).Sum();
}
