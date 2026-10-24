using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The spawn pools of the entity table. At the start of each tick the table reserves free slots for every chunk that has
/// live entities, in chunk order, so that a spawn during the tick takes its slot from its own chunk's pool and gets the
/// same handle in every run, whichever worker ran which chunk first. The tests here call the table in different chunk
/// orders to stand in for the workers.
/// </summary>
public class EntityTableSpawnPoolTests
{
    private const int ChunkSize = 4;

    /// <summary>
    /// A table with live entities in slots 0 to 4, so that chunk 0 (slots 0 to 3) and chunk 1 (slot 4) both have a
    /// live entity. Returns the two spawners: the entity in slot 0, which is in chunk 0, and the entity in slot 4, which
    /// is in chunk 1.
    /// </summary>
    private static EntityTable TwoChunkTable(out Entity spawnerInChunk0, out Entity spawnerInChunk1)
    {
        EntityTable table = TableSupport.NewTable();
        Entity[] entities = [.. Enumerable.Range(0, ChunkSize + 1).Select(_ => table.SpawnLive())];
        spawnerInChunk0 = entities[0];
        spawnerInChunk1 = entities[ChunkSize];
        return table;
    }

    private static uint[] SlotsOf(IEnumerable<Entity> spawned) => [.. spawned.Select(e => e.Index)];

    [Fact]
    public void SpawnsDuringATickTakeTheSpawnersPoolThenTheFallback()
    {
        EntityTable table = TableSupport.NewTable();
        Entity spawner = table.SpawnLive();

        table.BeginTick(spawnPoolSize: 2);
        Entity first = table.ReserveForSpawn(spawner);
        Entity second = table.ReserveForSpawn(spawner);
        Entity third = table.ReserveForSpawn(spawner);

        // The pool held the next two slots; the third spawn took a fresh slot past them.
        SlotsOf([first, second, third]).Should().Equal(1u, 2u, 3u);
        table.EndGameplay().Should().Be(1);
        table.AssertInvariants();
        foreach (Entity spawned in new[] { first, second, third })
            table.Activate(entity: spawned, row: default, render: new(), components: [], wants: [], body: null, bodyCenterOfMass: Vector3.Zero, ownedFrame: FrameHandle.Null);
        table.EndTick();
        table.LiveCount.Should().Be(4);
        table.AssertInvariants();
    }

    [Fact]
    public void AChunkThatSpawnedMoreLastTickGetsAPoolTwiceAsLarge()
    {
        EntityTable table = TableSupport.NewTable();
        Entity spawner = table.SpawnLive();
        table.BeginTick(spawnPoolSize: 2);
        Entity[] firstTick = [.. Enumerable.Range(0, 3).Select(_ => table.ReserveForSpawn(spawner))];
        table.EndGameplay();
        foreach (Entity spawned in firstTick)
            table.Activate(entity: spawned, row: default, render: new(), components: [], wants: [], body: null, bodyCenterOfMass: Vector3.Zero, ownedFrame: FrameHandle.Null);
        table.EndTick();

        // The chunk spawned three last tick, so its next pool holds six.
        table.BeginTick(spawnPoolSize: 2);
        for (int i = 0; i < 6; i++)
            table.ReserveForSpawn(spawner);

        table.EndGameplay().Should().Be(0);
        table.EndTick();
        table.AssertInvariants();
    }

    [Fact]
    public void AChunkCannotReserveMoreThan1024SlotsHoweverMuchItSpawnedLastTick()
    {
        EntityTable table = TableSupport.NewTable();
        Entity spawner = table.SpawnLive();
        table.BeginTick(spawnPoolSize: 2);
        Entity[] firstTick = [.. Enumerable.Range(0, 600).Select(_ => table.ReserveForSpawn(spawner))];
        table.EndGameplay();
        foreach (Entity spawned in firstTick)
            table.Activate(entity: spawned, row: default, render: new(), components: [], wants: [], body: null, bodyCenterOfMass: Vector3.Zero, ownedFrame: FrameHandle.Null);
        table.EndTick();

        // Twice 600 would be 1200 slots, but a pool stops at 1024: the 1025th spawn is the first to miss its pool.
        table.BeginTick(spawnPoolSize: 2);
        for (int i = 0; i < 1024; i++)
            table.ReserveForSpawn(spawner);
        table.ReserveForSpawn(spawner);

        table.EndGameplay().Should().Be(1);
        table.AssertInvariants();
    }

    [Fact]
    public void ASpawnPoolSizeOfZeroSendsEverySpawnToTheFallback()
    {
        EntityTable table = TableSupport.NewTable();
        Entity spawner = table.SpawnLive();

        table.BeginTick(spawnPoolSize: 0);
        Entity spawned = table.ReserveForSpawn(spawner);

        table.EndGameplay().Should().Be(1);
        table.Activate(entity: spawned, row: default, render: new(), components: [], wants: [], body: null, bodyCenterOfMass: Vector3.Zero, ownedFrame: FrameHandle.Null);
        table.IsAlive(spawned).Should().BeTrue();
        table.AssertInvariants();
    }

    [Fact]
    public void AFallbackSpawnIsLiveAndReadableOnceTheTickHasEnded()
    {
        EntityTable table = TableSupport.NewTable();
        Entity spawner = table.SpawnLive();
        table.BeginTick(spawnPoolSize: 0);
        Entity spawned = table.ReserveForSpawn(spawner);
        table.EndGameplay();

        table.Activate(entity: spawned, row: EntityTableSupport.RowAt(7), render: new(), components: [], wants: [], body: null, bodyCenterOfMass: Vector3.Zero, ownedFrame: FrameHandle.Null);
        table.EndTick();

        spawned.Generation.Should().Be(1u);
        table.IsAlive(spawned).Should().BeTrue();
        table.FrontRow(spawned).Pose.Position.X.Should().Be(7);
        table.AssertInvariants();
    }

    [Fact]
    public void EachChunkSpawnsFromItsOwnPoolWhicheverChunkSpawnsFirst()
    {
        EntityTable chunk0First = TwoChunkTable(out Entity a0, out Entity b0);
        EntityTable chunk1First = TwoChunkTable(out Entity a1, out Entity b1);
        chunk0First.BeginTick(spawnPoolSize: 2);
        chunk1First.BeginTick(spawnPoolSize: 2);

        // Chunk 0 and chunk 1 spawn two entities each, in opposite orders.
        Entity[] a0Spawns = [chunk0First.ReserveForSpawn(a0), chunk0First.ReserveForSpawn(a0)];
        Entity[] b0Spawns = [chunk0First.ReserveForSpawn(b0), chunk0First.ReserveForSpawn(b0)];
        Entity[] b1Spawns = [chunk1First.ReserveForSpawn(b1), chunk1First.ReserveForSpawn(b1)];
        Entity[] a1Spawns = [chunk1First.ReserveForSpawn(a1), chunk1First.ReserveForSpawn(a1)];

        // The pools were reserved in chunk order from the free slots 5 to 8: slots 5 and 6 for chunk 0, 7 and 8 for chunk 1.
        a0Spawns.Should().Equal(a1Spawns);
        b0Spawns.Should().Equal(b1Spawns);
        SlotsOf(a0Spawns).Should().Equal(5u, 6u);
        SlotsOf(b0Spawns).Should().Equal(7u, 8u);
    }

    [Fact]
    public void SpawnsInterleavedBetweenChunksGetTheSameHandlesAsAnyOtherOrder()
    {
        EntityTable table = TwoChunkTable(out Entity a, out Entity b);
        table.BeginTick(spawnPoolSize: 2);

        Entity b1 = table.ReserveForSpawn(b);
        Entity a1 = table.ReserveForSpawn(a);
        Entity b2 = table.ReserveForSpawn(b);
        Entity a2 = table.ReserveForSpawn(a);

        SlotsOf([a1, a2]).Should().Equal(5u, 6u);
        SlotsOf([b1, b2]).Should().Equal(7u, 8u);
    }

    [Fact]
    public void AChunkThatRanOutOfPoolSlotsDoesNotTakeThePoolSlotsOfAnotherChunk()
    {
        EntityTable table = TwoChunkTable(out Entity a, out Entity b);
        table.BeginTick(spawnPoolSize: 2);

        // Chunk 1 spawns four, two more than its pool holds. Chunk 0 then still finds its own two slots.
        Entity[] bSpawns = [.. Enumerable.Range(0, 4).Select(_ => table.ReserveForSpawn(b))];
        Entity[] aSpawns = [.. Enumerable.Range(0, 2).Select(_ => table.ReserveForSpawn(a))];

        SlotsOf(aSpawns).Should().Equal(5u, 6u);
        SlotsOf(bSpawns.Take(2)).Should().Equal(7u, 8u);
        SlotsOf(bSpawns.Skip(2)).Should().OnlyContain(slot => slot >= 9);
        table.EndGameplay().Should().Be(2);
    }

    [Fact]
    public void AnUnusedPoolSlotIsStillTheFirstSpawnSlotOnTheNextTick()
    {
        EntityTable table = TableSupport.NewTable();
        Entity spawner = table.SpawnLive();
        table.BeginTick(spawnPoolSize: 2);
        table.EndGameplay();
        table.EndTick();

        table.BeginTick(spawnPoolSize: 2);
        Entity spawned = table.ReserveForSpawn(spawner);

        spawned.Index.Should().Be(1u);
        table.EndGameplay().Should().Be(0);
        table.AssertInvariants();
    }

    [Fact]
    public void ASlotThatIsInAPoolIsNotHandedOutByAnOutsideTickReservation()
    {
        EntityTable table = TableSupport.NewTable();
        table.SpawnLive();
        table.BeginTick(spawnPoolSize: 2);
        table.EndGameplay();
        table.EndTick();

        Entity outsideTick = table.Reserve();

        // Slots 1 and 2 wait in the pool of chunk 0.
        outsideTick.Index.Should().Be(3u);
        table.AssertInvariants();
    }

    [Fact]
    public void ASlotReleasedBeforeATickComesOutOfThePoolWithTheNextGeneration()
    {
        EntityTable table = TableSupport.NewTable();
        Entity spawner = table.SpawnLive();
        Entity victim = table.SpawnLive();
        table.Release(victim);

        table.BeginTick(spawnPoolSize: 1);
        Entity spawned = table.ReserveForSpawn(spawner);

        spawned.Index.Should().Be(victim.Index);
        spawned.Generation.Should().Be(victim.Generation + 1);
        table.EndGameplay().Should().Be(0);
        table.AssertInvariants();
    }

    [Fact]
    public void AChunkWithNoLiveEntityLeavesItsFreeSlotsToTheChunksThatHaveOne()
    {
        EntityTable table = TableSupport.NewTable();
        Entity[] entities = [.. Enumerable.Range(0, ChunkSize + 1).Select(_ => table.SpawnLive())];
        // Empty chunk 0, so that only chunk 1 (slot 4) has a live entity.
        foreach (Entity entity in entities.Take(ChunkSize))
            table.Release(entity);

        table.BeginTick(spawnPoolSize: 2);
        Entity spawned = table.ReserveForSpawn(entities[ChunkSize]);

        // The pool of chunk 1 was reserved from the released slots, which chunk 0 left free, in release order.
        spawned.Index.Should().Be(entities[0].Index);
        spawned.Generation.Should().Be(entities[0].Generation + 1);
        table.EndGameplay().Should().Be(0);
        table.AssertInvariants();
    }
}
