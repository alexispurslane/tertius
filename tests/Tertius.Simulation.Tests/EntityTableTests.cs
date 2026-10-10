using System.Numerics;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The entity table on its own: the slot lifecycle, the columns that other subsystems add, the component masks, the
/// dirty tracking and the intent swap. Each test checks the table's invariants along the way.
/// </summary>
public class EntityTableTests
{
    private static EntityTable NewTable(int chunkSize = 4, int capacity = 8)
    {
        EngineEvents.Register();
        return new EntityTable(chunkSize, capacity);
    }

    private static Entity SpawnLive(EntityTable table, BodyDesc? body = null, params IComponent[] components)
    {
        Entity entity = table.Reserve();
        table.Activate(entity, new TransformRow { Pose = DPose.Identity }, new RenderData(), components, [.. components.Select(c => c.Wants)], body, FrameHandle.Null);
        return entity;
    }

    private static TransformRow RowAt(double x) => new() { Pose = TestWorld.At(x) };

    private static List<Entity> LiveEntitiesOfChunk(EntityTable table, int chunk)
    {
        var entities = new List<Entity>();
        foreach (Entity entity in table.LiveEntitiesOfChunk(chunk))
            entities.Add(entity);
        return entities;
    }

    [Fact]
    public void ASlotIsReservedThenLiveThenFreeAndEveryOldHandleGoesStale()
    {
        EntityTable table = NewTable();
        Entity entity = table.Reserve();
        Assert.False(table.IsAlive(entity));
        Assert.Equal(1, table.SlotCount);
        table.AssertInvariants();

        table.Activate(entity, RowAt(2), new RenderData(), [new Recorder()], [EventMask.Of<Ping>()], null, FrameHandle.Null);
        Assert.True(table.IsAlive(entity));
        Assert.Equal(1, table.LiveCount);
        Assert.Equal(2, table.FrontRow(entity).Pose.Position.X);
        Assert.Equal(2, table.BackRow(entity).Pose.Position.X);
        table.AssertInvariants();

        Assert.True(table.Release(entity));
        Assert.False(table.IsAlive(entity));
        Assert.Equal(0, table.LiveCount);
        Assert.Throws<StaleEntityException>(() => table.Components(entity).Length);
        table.AssertInvariants();

        Entity next = table.Reserve();
        Assert.Equal(entity.Index, next.Index);
        Assert.Equal(entity.Generation + 1, next.Generation);
    }

    [Fact]
    public void AReservationThatNeverBecomesLiveCanBeReleased()
    {
        EntityTable table = NewTable();
        Entity dropped = table.Reserve();
        Assert.True(table.Release(dropped));
        table.AssertInvariants();

        // The handle that the spawner got is stale, and the slot comes back with a new generation.
        Entity next = table.Reserve();
        Assert.Equal(dropped.Index, next.Index);
        Assert.NotEqual(dropped.Generation, next.Generation);
        Assert.Equal(0, table.LiveCount);
    }

    [Fact]
    public void ColumnsGrowWithTheTableAndAReleasedSlotKeepsNothingInThem()
    {
        EntityTable table = NewTable(capacity: 2);
        var released = new List<string>();
        EntityTable.Column<int> numbers = null!;
        numbers = table.AddColumn<int>(entity => released.Add($"numbers {entity.Index}: {numbers[entity]}"));
        EntityTable.Column<string?> names = null!;
        names = table.AddColumn<string?>(entity => released.Add($"names {entity.Index}: {names[entity]}"));
        EntityTable.Column<long> silent = table.AddColumn<long>();

        var all = new List<Entity>();
        for (int i = 0; i < 100; i++)
        {
            Entity entity = SpawnLive(table);
            numbers[entity] = i + 1;
            names[entity] = $"e{i}";
            silent[entity] = -1;
            all.Add(entity);
        }

        // Every column covers every slot the table grew to.
        Assert.True(table.Capacity >= 100);
        Assert.Equal(table.Capacity, numbers.AsSpan().Length);
        Assert.Equal(table.Capacity, silent.AsSpan().Length);

        // The owners let go first, in the order the columns were added, while the entries still hold their values.
        table.Release(all[10]);
        Assert.Equal(["numbers 10: 11", "names 10: e10"], released);
        Assert.Equal(0, numbers.AsSpan()[10]);
        Assert.Null(names.AsSpan()[10]);
        Assert.Equal(0, silent.AsSpan()[10]);
        table.AssertInvariants();

        // A new entity in the reused slot starts from clear entries.
        Entity reused = SpawnLive(table);
        Assert.Equal(all[10].Index, reused.Index);
        Assert.Equal(0, numbers[reused]);
    }

    [Fact]
    public void AComponentChangeKeepsTheMaskInStep()
    {
        EntityTable table = NewTable();
        var pings = new Recorder(EventMask.Of<Ping>());
        Entity entity = SpawnLive(table, null, pings);
        Assert.True(table.Accepts(entity, EventType<Ping>.Id));
        Assert.False(table.Accepts(entity, EventType<Heal>.Id));
        // Every mask has Destroy, so that a Destroy always gets through.
        Assert.True(table.Accepts(entity, EventType<Destroy>.Id));

        var heals = new Recorder(EventMask.Of<Heal>(), priority: -1);
        table.AddComponent(entity, heals);
        Assert.Equal([heals, pings], table.Components(entity).ToArray());
        Assert.Equal([heals.Wants, pings.Wants], table.ComponentWants(entity).ToArray());
        Assert.True(table.Accepts(entity, EventType<Heal>.Id));
        Assert.Throws<InvalidOperationException>(() => table.AddComponent(entity, heals));
        table.AssertInvariants();

        Assert.True(table.RemoveComponent(entity, pings));
        Assert.False(table.RemoveComponent(entity, pings));
        Assert.False(table.Accepts(entity, EventType<Ping>.Id));
        Assert.True(table.Accepts(entity, EventType<Heal>.Id));
        table.AssertInvariants();

        // A released entity cannot be asked at all: its handle is stale. The router never asks one.
        table.Release(entity);
        Assert.Throws<StaleEntityException>(() => table.Accepts(entity, EventType<Destroy>.Id));
    }

    [Fact]
    public void ASyncWriteIsCopiedForwardOnlyWhenItChangesTheRow()
    {
        EntityTable table = NewTable();
        Entity entity = SpawnLive(table);
        Assert.Equal([entity], table.TakeMoved().ToArray());
        Assert.True(table.TakeMoved().IsEmpty);

        table.SetBackTransform(entity, RowAt(5));
        Assert.Equal(5, table.BackRow(entity).Pose.Position.X);
        Assert.Equal(0, table.FrontRow(entity).Pose.Position.X);
        table.AssertInvariants();

        table.CopyDirtyRows();
        Assert.Equal(5, table.FrontRow(entity).Pose.Position.X);
        Assert.Equal([entity], table.TakeMoved().ToArray());
        table.AssertInvariants();

        // The same row again changes nothing, so the entity does not move.
        table.SetBackTransform(entity, RowAt(5));
        table.CopyDirtyRows();
        Assert.True(table.TakeMoved().IsEmpty);
        table.AssertInvariants();
    }

    [Fact]
    public void AGameplayWriteIsCopiedForwardAtTheSyncAndForgottenAfterIt()
    {
        EntityTable table = NewTable();
        Entity entity = SpawnLive(table);
        table.TakeMoved();

        table.BeginTick(spawnPoolSize: 0);
        Assert.True(table.TakeFirstTick(entity));
        Assert.False(table.TakeFirstTick(entity));
        table.WritableTransform(entity) = TestWorld.At(3);
        table.WritableRender(entity).Scale = new Vector3(2);
        Assert.Equal(0, table.FrontRow(entity).Pose.Position.X);
        Assert.Equal(0, table.EndGameplay());
        Assert.True(table.GameplayWroteTransform(entity));
        table.AssertInvariants();

        table.CopyDirtyRows();
        table.EndTick();
        Assert.Equal(3, table.FrontRow(entity).Pose.Position.X);
        Assert.Equal(new Vector3(2), table.FrontRender(entity).Scale);
        Assert.False(table.GameplayWroteTransform(entity));
        Assert.Equal([entity], table.TakeMoved().ToArray());
        table.AssertInvariants();
    }

    [Fact]
    public void TheIntentListDropsEntitiesThatDiedAfterTheSwap()
    {
        EntityTable table = NewTable();
        Entity a = SpawnLive(table, TestWorld.Ball);
        Entity b = SpawnLive(table, TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(b).Force = new Vector3(2, 0, 0);
        table.WritableIntent(a).Force = new Vector3(1, 0, 0);
        Assert.Equal(new Vector3(1, 0, 0), table.PendingIntent(a).Force);
        table.EndGameplay();
        table.SwapIntents(keep: true);
        table.EndTick();

        // In the order the entities first wrote their intents.
        Assert.Equal([b, a], table.IntentEntities.ToArray());
        Assert.Equal(new Vector3(1, 0, 0), table.FrontIntents.Span[(int)a.Index].Force);
        Assert.Equal(default, table.PendingIntent(a));
        table.AssertInvariants();

        table.Release(a);
        Assert.Equal([b], table.IntentEntities.ToArray());
        Assert.Equal(default, table.FrontIntents.Span[(int)a.Index]);
        table.AssertInvariants();

        // A swap with nothing written clears the list and the intents it named.
        table.BeginTick(spawnPoolSize: 0);
        table.EndGameplay();
        table.SwapIntents(keep: true);
        Assert.True(table.IntentEntities.IsEmpty);
        Assert.Equal(default, table.FrontIntents.Span[(int)b.Index]);
        table.AssertInvariants();
    }

    [Fact]
    public void SpawnsDuringATickTakeTheSpawnersPoolThenTheFallback()
    {
        EntityTable table = NewTable(chunkSize: 4);
        Entity spawner = SpawnLive(table);

        table.BeginTick(spawnPoolSize: 2);
        Entity first = table.ReserveForSpawn(spawner);
        Entity second = table.ReserveForSpawn(spawner);
        Entity third = table.ReserveForSpawn(spawner);
        // The pool held the next two slots; the third spawn took a fresh slot past them.
        Assert.Equal([1u, 2u, 3u], new[] { first.Index, second.Index, third.Index });
        Assert.Equal(1, table.EndGameplay());
        table.AssertInvariants();

        foreach (Entity spawned in new[] { first, second, third })
            table.Activate(spawned, default, new RenderData(), [], [], null, FrameHandle.Null);
        table.EndTick();
        Assert.Equal(4, table.LiveCount);
        table.AssertInvariants();

        // The chunk spawned three last tick, so its next pool holds six.
        table.BeginTick(spawnPoolSize: 2);
        for (int i = 0; i < 6; i++)
            table.ReserveForSpawn(spawner);
        Assert.Equal(0, table.EndGameplay());
        table.EndTick();
    }

    [Fact]
    public void AChunkWithNoLiveEntityReturnsItsPoolAndDoesNotRun()
    {
        EntityTable table = NewTable(chunkSize: 2);
        Entity a = SpawnLive(table);
        table.BeginTick(spawnPoolSize: 1);
        Assert.Equal([a], LiveEntitiesOfChunk(table, 0));
        table.EndGameplay();
        table.EndTick();

        table.Release(a);
        table.BeginTick(spawnPoolSize: 1);
        Assert.Empty(LiveEntitiesOfChunk(table, 0));
        table.EndGameplay();
        table.EndTick();
        table.AssertInvariants();
        // Both slots are free again: the released one and the pool slot the chunk gave back.
        Assert.NotEqual(a, table.Reserve());
        table.Reserve();
        Assert.Equal(2, table.SlotCount);
    }

#if TERTIUS_CONTRACTS
    [Fact]
    public void LifecycleStepsOutOfOrderBreakAContract()
    {
        EntityTable table = NewTable();
        Entity live = SpawnLive(table);
        Entity reserved = table.Reserve();

        Assert.Throws<ContractException>(() => table.Activate(live, default, new RenderData(), [], [], null, FrameHandle.Null));

        table.BeginTick(spawnPoolSize: 0);
        Assert.Throws<ContractException>(() => table.Reserve());
        Assert.Throws<ContractException>(() => table.Release(reserved));
        Assert.Throws<ContractException>(() => table.SetBackTransform(live, RowAt(1)));
        Assert.Throws<ContractException>(() => table.WritableIntent(live));
        table.EndGameplay();
        Assert.Throws<ContractException>(() => table.WritableTransform(live));
    }
#endif
}
