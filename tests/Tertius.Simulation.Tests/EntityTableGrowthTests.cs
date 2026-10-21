using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.EntityTableSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The table grows when it runs out of slots, outside a tick or at the end of the gameplay part of one. Growing copies
/// every per-slot table and every column into larger arrays, so a row or an entry that is lost in the copy would be
/// lost silently. These tests fill the table well past its first allocation and check that nothing changed.
/// </summary>
public class EntityTableGrowthTests
{
    /// <summary>How many more entities than the table's first allocation each test creates, so that it grows at least once.</summary>
    private const int Beyond = 200;

    private static readonly FrameHandle OwnedFrame = new(new Entity(900, 1), 3, 1);

    /// <summary>
    /// Makes <paramref name="count"/> live entities with distinct data: entity i is at x = i with a render scale of
    /// i + 1, has a Recorder that wants Ping, and has a body if i is odd and an owned frame if i is a multiple of three.
    /// </summary>
    private static Entity[] SpawnDistinct(EntityTable table, int count) => [.. Enumerable.Range(0, count).Select(i =>
    {
        Entity entity = table.Reserve();
        table.Activate(entity, RowAt(i), new RenderData { Scale = new Vector3(i + 1) }, [new Recorder()], [EventMask.Of<Ping>()],
            i % 2 == 1 ? TestWorld.Ball : null, Vector3.Zero, i % 3 == 0 ? OwnedFrame : FrameHandle.Null);
        return entity;
    })];

    [Fact]
    public void GrowingTheTablesKeepsEveryRowOfEveryEntity()
    {
        EntityTable table = TableSupport.NewTable();
        Entity[] early = SpawnDistinct(table, 10);
        int capacityBefore = table.Capacity;

        for (int i = 0; i < Beyond; i++)
            table.SpawnLive();

        table.Capacity.Should().BeGreaterThan(capacityBefore);
        for (int i = 0; i < early.Length; i++)
        {
            Entity entity = early[i];
            table.FrontRow(entity).Pose.Position.X.Should().Be(i);
            table.BackRow(entity).Pose.Position.X.Should().Be(i);
            table.FrontRender(entity).Scale.Should().Be(new Vector3(i + 1));
            table.Components(entity).Length.Should().Be(1);
            table.Accepts(entity, EventType<Ping>.Id).Should().BeTrue();
            table.Accepts(entity, EventType<Heal>.Id).Should().BeFalse();
            (table.Body(entity) != null).Should().Be(i % 2 == 1);
            table.OwnedFrame(entity).Should().Be(i % 3 == 0 ? OwnedFrame : FrameHandle.Null);
        }
        table.LiveCount.Should().Be(10 + Beyond);
        table.AssertInvariants();
    }

    [Fact]
    public void GrowingTheTablesKeepsABackRowThatWasWrittenButNotCopiedYet()
    {
        EntityTable table = TableSupport.NewTable();
        Entity[] early = SpawnDistinct(table, 10);
        table.TakeMoved();
        foreach (Entity entity in early)
            table.SetBackTransform(entity, RowAt(1000 + entity.Index));

        for (int i = 0; i < Beyond; i++)
            table.SpawnLive();
        table.CopyDirtyRows();

        foreach (Entity entity in early)
            table.FrontRow(entity).Pose.Position.X.Should().Be(1000 + entity.Index);
        table.AssertInvariants();
    }

    [Fact]
    public void GrowingTheTablesKeepsEveryColumnEntry()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        EntityTable.Column<string?> names = table.AddColumn<string?>();
        EntityTable.Column<List<int>?> lists = table.AddColumn<List<int>?>();
        Entity[] early = [.. Enumerable.Range(0, 10).Select(_ => table.SpawnLive())];
        for (int i = 0; i < early.Length; i++)
        {
            numbers[early[i]] = 100 + i;
            names[early[i]] = $"name {i}";
            lists[early[i]] = [i, i * 2];
        }

        for (int i = 0; i < Beyond; i++)
            table.SpawnLive();

        for (int i = 0; i < early.Length; i++)
        {
            numbers[early[i]].Should().Be(100 + i);
            names[early[i]].Should().Be($"name {i}");
            lists[early[i]].Should().Equal(i, i * 2);
        }
        numbers.AsSpan().Length.Should().Be(table.Capacity);
        names.AsSpan().Length.Should().Be(table.Capacity);
        lists.AsSpan().Length.Should().Be(table.Capacity);
        table.AssertInvariants();
    }

    [Fact]
    public void ASlotPastTheOldCapacityStartsWithClearColumnEntries()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        int oldCapacity = table.Capacity;
        Entity[] entities = [.. Enumerable.Range(0, oldCapacity).Select(_ => table.SpawnLive())];
        foreach (Entity entity in entities)
            numbers[entity] = 7;

        Entity beyond = table.SpawnLive();

        beyond.Index.Should().BeGreaterThanOrEqualTo((uint)oldCapacity);
        numbers[beyond].Should().Be(0);
        table.AssertInvariants();
    }

    [Fact]
    public void GrowingKeepsTheIntentsThatThePhysicsStepWillRead()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball), b = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(a).Force = new Vector3(1, 0, 0);
        table.WritableIntent(b).Force = new Vector3(2, 0, 0);
        table.EndGameplay();
        table.SwapIntents(keep: true);
        table.EndTick();

        for (int i = 0; i < Beyond; i++)
            table.SpawnLive();

        table.IntentEntities.ToArray().Should().Equal(a, b);
        table.FrontIntents.Span[(int)a.Index].Force.Should().Be(new Vector3(1, 0, 0));
        table.FrontIntents.Span[(int)b.Index].Force.Should().Be(new Vector3(2, 0, 0));
        table.FrontIntents.Length.Should().BeGreaterThanOrEqualTo(table.SlotCount);
        table.AssertInvariants();
    }

    [Fact]
    public void GrowingAtTheEndOfAGameplayPartKeepsTheRowsAndIntentsThatGameplayWroteMeanwhile()
    {
        EntityTable table = TableSupport.NewTable();
        Entity[] entities = [.. Enumerable.Range(0, 3).Select(_ => table.SpawnLiveWith(null))];
        Entity bodied = table.SpawnLiveWith(TestWorld.Ball);
        table.TakeMoved();
        int capacityBefore = table.Capacity;

        // Gameplay writes the rows and the intent first, then spawns past the pool so that the tables must grow.
        table.BeginTick(spawnPoolSize: 0);
        table.WritableTransform(entities[0]) = TestWorld.At(5);
        table.WritableRender(entities[1]).Scale = new Vector3(4);
        table.WritableIntent(bodied).Force = new Vector3(0, 9, 0);
        Entity[] spawned = [.. Enumerable.Range(0, Beyond).Select(_ => table.ReserveForSpawn(entities[2]))];
        table.EndGameplay().Should().Be(Beyond);

        table.Capacity.Should().BeGreaterThan(capacityBefore);
        table.PendingIntent(bodied).Force.Should().Be(new Vector3(0, 9, 0));
        table.AssertInvariants();
        foreach (Entity entity in spawned)
            table.Activate(entity, default, new RenderData(), [], [], null, Vector3.Zero, FrameHandle.Null);
        table.SwapIntents(keep: true);
        table.CopyDirtyRows();
        table.EndTick();

        table.FrontRow(entities[0]).Pose.Position.X.Should().Be(5);
        table.FrontRender(entities[1]).Scale.Should().Be(new Vector3(4));
        table.FrontIntents.Span[(int)bodied.Index].Force.Should().Be(new Vector3(0, 9, 0));
        table.IntentEntities.ToArray().Should().Equal(bodied);
        table.LiveCount.Should().Be(4 + Beyond);
        table.AssertInvariants();
    }

    [Fact]
    public void EverySlotEverHandedOutIsCoveredByTheCapacity()
    {
        EntityTable table = TableSupport.NewTable();

        for (int i = 0; i < Beyond; i++)
        {
            table.SpawnLive();
            table.Capacity.Should().BeGreaterThanOrEqualTo(table.SlotCount);
        }

        table.SlotCount.Should().Be(Beyond);
    }

    [Fact]
    public void TheTablesAtLeastDoubleWhenTheyGrowSoThatGrowingStaysRare()
    {
        EntityTable table = TableSupport.NewTable();
        var capacities = new List<int> { table.Capacity };

        for (int i = 0; i < 2000; i++)
        {
            table.SpawnLive();
            if (table.Capacity != capacities[^1])
                capacities.Add(table.Capacity);
        }

        capacities.Should().HaveCountGreaterThan(1);
        capacities.Zip(capacities.Skip(1)).Should().OnlyContain(pair => pair.Second >= 2 * pair.First);
    }

    [Fact]
    public void AGrownTableHasAChunkForEverySlotItCanHold()
    {
        EntityTable table = TableSupport.NewTable();

        for (int i = 0; i < Beyond; i++)
            table.SpawnLive();

        // Chunks are four slots long in this table. Every slot that was handed out is in a chunk that can be run.
        table.Chunks.Length.Should().Be((table.SlotCount + 3) / 4);
        table.LiveEntitiesOfChunk(table.Chunks.Length - 1).ToList().Should().NotBeEmpty();
    }
}
