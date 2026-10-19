using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.EntityTableSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The dirty-row copy on the workers. When the rows that gameplay wrote reach the table's threshold, the copy of those
/// rows runs on the workers, one chunk at a time, and what the copy on one thread appends to the table's shared lists (the
/// moved entities and the component flag changes) joins those lists in chunk order afterwards. The copy on the workers
/// must end in the same table as the copy on one thread, bit for bit. So this class runs every test of
/// <see cref="EntityTableDirtyRowTests"/> again on a table whose threshold is at its lowest, one row, and compares the two
/// copies over a few ticks of writes in many chunks.
/// </summary>
public class EntityTableParallelCopyTests : EntityTableDirtyRowTests
{
    private const int ChunkSize = 4;

    /// <summary>A table like the one of the base class, which copies the rows that gameplay wrote on four workers from one row up.</summary>
    private protected override EntityTable NewTable() => NewTable(parallel: true);

    private static EntityTable NewTable(bool parallel)
    {
        EngineEvents.Register();
        return parallel
            ? new EntityTable(ChunkSize, initialCapacity: 8, new ParallelOptions { MaxDegreeOfParallelism = 4 }) { ParallelCopyThreshold = 1 }
            : new EntityTable(ChunkSize, initialCapacity: 8);
    }

    /// <summary>What a copy leaves behind: the moved list, every live entity's front rows, and the component index.</summary>
    private sealed record CopyResult(Entity[] Moved, (Entity, TransformRow, RenderData, ComponentFlags)[] Rows, Entity[][] Index);

    private static int Id<T>() where T : IComponent => ComponentTypes.IdOf(typeof(T));

    /// <summary>
    /// Three ticks over 48 entities in 12 chunks, the same on any table: gameplay writes transforms, render rows and
    /// components in an order that jumps between chunks, and writes some entities twice; then the sync point releases a
    /// written entity and spawns a new one into its slot, writes a row that gameplay wrote and one that it did not, and
    /// spawns an entity in a fresh slot. Returns what each tick's copy left behind.
    /// </summary>
    private static List<CopyResult> RunScript(EntityTable table)
    {
        var results = new List<CopyResult>();
        var entities = new List<Entity>();
        for (int i = 0; i < 12 * ChunkSize; i++)
        {
            IComponent[] components = (i % 3) switch { 0 => [new Metal()], 1 => [new Wet(), new Metal()], _ => [] };
            entities.Add(table.SpawnLiveAt(i, null, components));
        }
        table.TakeMoved();

        for (int tick = 0; tick < 3; tick++)
        {
            table.BeginTick(spawnPoolSize: 0);
            // A stride of 13, which shares no factor with the 48 to 50 entities, visits every chunk in a scrambled order.
            for (int k = 0; k < entities.Count; k++)
            {
                Entity e = entities[(k * 13 + tick) % entities.Count];
                if (!table.IsAlive(e))
                    continue;
                if (k % 2 == 0)
                    table.WritableTransform(e) = TestWorld.At(100 * tick + k);
                if (k % 3 == 0)
                    table.WritableRender(e).Scale = new Vector3(tick + k);
                if (k % 5 == 0)
                    table.AddComponent(e, new Flammable());
                if (k % 11 == 0)
                    table.WritableTransform(e) = TestWorld.At(-k);
            }
            table.EndGameplay();

            Entity dying = entities[(tick * 13 + 2) % entities.Count];
            table.Release(dying);
            entities[entities.IndexOf(dying)] = table.SpawnLiveAt(1000 + tick, null, new Wet());
            table.SetBackTransform(entities[(tick * 5 + 1) % entities.Count], RowAt(2000 + tick));
            table.SetBackTransform(entities[(tick * 5 + 4) % entities.Count], RowAt(3000 + tick));
            entities.Add(table.SpawnLiveAt(4000 + tick));

            table.CopyDirtyRows();
            table.AssertInvariants();
            results.Add(new CopyResult(
                table.TakeMoved().ToArray(),
                [.. entities.Where(table.IsAlive).Select(e => (e, table.FrontRow(e), table.FrontRender(e), table.FrontComponents(e)))],
                [table.WithComponent(Id<Metal>()).ToArray(), table.WithComponent(Id<Wet>()).ToArray(), table.WithComponent(Id<Flammable>()).ToArray()]));
            table.SwapIntents(keep: false);
            table.EndTick();
        }
        return results;
    }

    [Fact]
    public void ACopyOnTheWorkersEndsInTheTableOfTheCopyOnOneThread()
    {
        List<CopyResult> serial = RunScript(NewTable(parallel: false));
        List<CopyResult> parallel = RunScript(NewTable(parallel: true));

        parallel.Should().HaveCount(serial.Count);
        for (int tick = 0; tick < serial.Count; tick++)
        {
            parallel[tick].Moved.Should().Equal(serial[tick].Moved, $"tick {tick} must list the moved entities in the serial order");
            parallel[tick].Rows.Should().Equal(serial[tick].Rows, $"tick {tick} must copy the same front rows");
            for (int type = 0; type < serial[tick].Index.Length; type++)
                parallel[tick].Index[type].Should().Equal(serial[tick].Index[type], $"tick {tick} must index the same entities by component type {type}");
        }
    }

    [Fact]
    public void TheScriptWritesManyChunksSoThatTheComparisonMeansSomething()
    {
        List<CopyResult> serial = RunScript(NewTable(parallel: false));

        serial.Should().AllSatisfy(result => result.Moved.Length.Should().BeGreaterThan(20));
        serial[^1].Index[2].Should().NotBeEmpty("gameplay added components, which the copy indexes");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AThresholdOfTheCopyOnTheWorkersBelowOneRowIsRefused(int threshold)
    {
        EntityTable table = NewTable(parallel: false);

        Action set = () => table.ParallelCopyThreshold = threshold;

        set.Should().Throw<ArgumentOutOfRangeException>();
        table.ParallelCopyThreshold.Should().Be(EntityTable.DefaultParallelCopyThreshold, "a refused threshold changes nothing");
    }
}
