using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The component flags that the snapshot publishes: a component asks whether another entity had a component of a type
/// at the end of the previous tick. A component that is added or removed shows in the next tick, whether a component
/// changed it during a tick or the game changed it between ticks.
/// </summary>
public sealed class ComponentFlagsWorldTests
{
    /// <summary>A component that records, on each tick, whether <paramref name="target"/> had a <typeparamref name="T"/> in the snapshot.</summary>
    private static Lambda Watches<T>(Func<Entity> target, List<bool> seen) where T : IComponent =>
        Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            seen.Add(self.Snapshot.HasComponent<T>(target())));

    [Fact]
    public void AComponentSeesWhichComponentsAnotherEntityHad()
    {
        using var t = new TestWorld();
        Entity target = t.SpawnWith(new Flammable(), new Metal());
        var flammable = new List<bool>();
        var wet = new List<bool>();
        t.SpawnWith(Watches<Flammable>(() => target, flammable), Watches<Wet>(() => target, wet));

        t.World.Tick();

        flammable.Should().Equal(true);
        wet.Should().Equal(false);
    }

    [Fact]
    public void AComponentThatAnEntityAddsDuringATickShowsInTheNextTick()
    {
        using var t = new TestWorld();
        Entity target = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 1)
                self.AddComponent(new Flammable());
        }));
        var seen = new List<bool>();
        // Spawned after the target, so it runs after it in the same chunk: it still reads the end of the previous tick.
        t.SpawnWith(Watches<Flammable>(() => target, seen));

        t.World.Run(3);

        seen.Should().Equal(false, false, true);
    }

    [Fact]
    public void AComponentThatAnEntityRemovesDuringATickLeavesInTheNextTick()
    {
        var flammable = new Flammable();
        using var t = new TestWorld();
        Entity target = t.SpawnWith(flammable, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 1)
                self.RemoveComponent(self.GetComponent<Flammable>()!);
        }));
        var seen = new List<bool>();
        t.SpawnWith(Watches<Flammable>(() => target, seen));

        t.World.Run(3);

        seen.Should().Equal(true, true, false);
    }

    [Fact]
    public void AnEntityWithTwoComponentsOfATypeKeepsTheFlagWhenOneLeaves()
    {
        var first = new Metal();
        using var t = new TestWorld();
        Entity target = t.SpawnWith(first, new Metal());

        t.World.RemoveComponent(target, first);

        t.World.Snapshot.HasComponent<Metal>(target).Should().BeTrue();
    }

    [Fact]
    public void AComponentThatTheGameAddsOrRemovesBetweenTicksShowsInTheNextTick()
    {
        using var t = new TestWorld();
        Entity target = t.SpawnWith(new Metal());
        var seen = new List<bool>();
        t.SpawnWith(Watches<Flammable>(() => target, seen));
        var flammable = new Flammable();

        t.World.Tick();
        t.World.AddComponent(target, flammable);
        t.World.Tick();
        t.World.RemoveComponent(target, flammable);
        t.World.Tick();

        seen.Should().Equal(false, true, false);
    }

    [Fact]
    public void ADeadEntityHasNoComponents()
    {
        using var t = new TestWorld();
        Entity target = t.SpawnWith(new Flammable());

        t.World.Destroy(target);

        t.World.Snapshot.HasComponent<Flammable>(target).Should().BeFalse();
        t.World.Snapshot.HasComponent<Flammable>(Entity.Null).Should().BeFalse();
    }

    [Fact]
    public void AnEntitySpawnedDuringATickHasItsFlagsFromItsFirstTick()
    {
        using var t = new TestWorld();
        Entity child = default;
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                child = self.Spawn(new() { Frame = self.Frame, Components = [new Crew()] });
        }));

        t.World.Tick();

        t.World.Snapshot.HasComponent<Crew>(child).Should().BeTrue();
    }

    [Fact]
    public void ManyWorkersReadTheFlagsAtOnceWhileEntitiesChangeTheirOwnAndAllSeeTheEndOfThePreviousTick()
    {
        // 200 entities toggle Flammable on themselves on every tick, and each counts the Flammable entities among its
        // neighbors in the snapshot. With eight workers the counts must equal the counts of one worker.
        List<int> Run(int workers)
        {
            using var t = new TestWorld(maxDegreeOfParallelism: workers, chunkSize: 4);
            var counts = new List<int>();
            var lambdas = new List<Lambda>();
            var entities = new Entity[200];
            for (int i = 0; i < entities.Length; i++)
            {
                int me = i;
                lambdas.Add(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
                {
                    int seen = 0;
                    for (int n = Math.Max(0, me - 3); n < Math.Min(entities.Length, me + 4); n++)
                        if (self.Snapshot.HasComponent<Flammable>(entities[n]))
                            seen++;
                    lock (counts)
                    {
                        counts.Add((int)(self.Tick * 100_000 + me * 10 + seen));
                    }

                    if ((self.Tick + me) % 3 == 0)
                    {
                        if (self.GetComponent<Flammable>() is { } flammable)
                            self.RemoveComponent(flammable);
                        else
                            self.AddComponent(new Flammable());
                    }
                }));
                entities[i] = t.SpawnAt(new(i, 0, 0), lambdas[i]);
            }

            t.World.Run(12);
            t.World.Errors.Should().BeEmpty();
            counts.Sort();
            return counts;
        }

        Run(8).Should().Equal(Run(1));
    }
}
