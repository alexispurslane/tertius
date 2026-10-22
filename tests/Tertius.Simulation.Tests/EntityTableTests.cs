using System.Numerics;
using System.Reflection;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.EntityTableSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The entity table on its own: the slot lifecycle, the columns that other subsystems add, the component masks, the
/// dirty tracking and the intent swap. Each test checks the table's invariants along the way. The spawn pools, the
/// growth of the tables, the dirty-row copy and the handling of stale handles have their own files.
/// </summary>
public class EntityTableTests
{
    [Fact]
    public void AReservedSlotIsNotLiveUntilItIsActivated()
    {
        EntityTable table = TableSupport.NewTable();

        Entity entity = table.Reserve();

        table.IsAlive(entity).Should().BeFalse();
        table.LiveCount.Should().Be(0);
        table.SlotCount.Should().Be(1);
        table.AssertInvariants();
    }

    [Fact]
    public void ActivatingAReservationMakesTheEntityLiveWithItsRowInBothCopies()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.Reserve();

        table.Activate(entity, RowAt(2), new(), [new Recorder()], [EventMask.Of<Ping>()], null, Vector3.Zero,
            FrameHandle.Null);

        table.IsAlive(entity).Should().BeTrue();
        table.LiveCount.Should().Be(1);
        table.FrontRow(entity).Pose.Position.X.Should().Be(2);
        table.BackRow(entity).Pose.Position.X.Should().Be(2);
        table.AssertInvariants();
    }

    [Fact]
    public void ReleasingAnEntityMakesItsHandleStale()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.SpawnLive();

        table.Release(entity).Should().BeTrue();

        table.IsAlive(entity).Should().BeFalse();
        table.LiveCount.Should().Be(0);
        Action read = () => _ = table.Components(entity).Length;
        read.Should().Throw<StaleEntityException>();
        table.AssertInvariants();
    }

    [Fact]
    public void AReleasedSlotComesBackWithTheNextGeneration()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.SpawnLive();
        table.Release(entity);

        Entity next = table.Reserve();

        next.Index.Should().Be(entity.Index);
        next.Generation.Should().Be(entity.Generation + 1);
    }

    [Fact]
    public void EveryRoundOfReleaseAndReuseOfOneSlotRaisesTheGenerationByOne()
    {
        EntityTable table = TableSupport.NewTable();
        var handles = new List<Entity> { table.SpawnLive() };

        for (int round = 0; round < 5; round++)
        {
            table.Release(handles[^1]);
            handles.Add(table.SpawnLive());
        }

        handles.Select(h => h.Index).Distinct().Should().ContainSingle();
        handles.Select(h => h.Generation).Should().Equal(1u, 2u, 3u, 4u, 5u, 6u);
        handles.Take(5).Should().OnlyContain(old => !table.IsAlive(old));
        table.IsAlive(handles[^1]).Should().BeTrue();
        table.AssertInvariants();
    }

    [Fact]
    public void ASlotThatWasNeverUsedStartsAtGenerationOne()
    {
        EntityTable table = TableSupport.NewTable();

        Entity first = table.Reserve();

        first.Generation.Should().Be(1u);
        first.IsNull.Should().BeFalse();
    }

    [Fact]
    public void ReleasedSlotsComeBackInTheOrderTheyWereReleased()
    {
        // First in, first out: the order in which slots are reused depends only on the order of the releases, which the
        // sync point fixes, so the handles of later spawns are the same in every run.
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLive(), b = table.SpawnLive(), c = table.SpawnLive();
        table.Release(c);
        table.Release(a);
        table.Release(b);

        uint[] reused = [table.Reserve().Index, table.Reserve().Index, table.Reserve().Index];

        reused.Should().Equal(c.Index, a.Index, b.Index);
    }

    [Fact]
    public void AReservationThatNeverBecomesLiveCanBeReleased()
    {
        EntityTable table = TableSupport.NewTable();
        Entity dropped = table.Reserve();

        table.Release(dropped).Should().BeTrue();

        table.AssertInvariants();
        // The handle that the spawner got is stale, and the slot comes back with a new generation.
        Entity next = table.Reserve();
        next.Index.Should().Be(dropped.Index);
        next.Generation.Should().NotBe(dropped.Generation);
        table.LiveCount.Should().Be(0);
    }

    [Fact]
    public void ColumnsGrowWithTheTable()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        EntityTable.Column<long> silent = table.AddColumn<long>();

        for (int i = 0; i < 100; i++)
            numbers[table.SpawnLive()] = i + 1;

        table.Capacity.Should().BeGreaterThanOrEqualTo(100);
        numbers.AsSpan().Length.Should().Be(table.Capacity);
        silent.AsSpan().Length.Should().Be(table.Capacity);
        table.AssertInvariants();
    }

    [Fact]
    public void ReleaseHooksRunInColumnOrderWhileTheEntriesStillHoldTheirValues()
    {
        EntityTable table = TableSupport.NewTable();
        var released = new List<string>();
        EntityTable.Column<int> numbers = null!;
        numbers = table.AddColumn<int>(entity => released.Add($"numbers {entity.Index}: {numbers[entity]}"));
        EntityTable.Column<string?> names = null!;
        names = table.AddColumn<string?>(entity => released.Add($"names {entity.Index}: {names[entity]}"));
        Entity entity = table.SpawnLive();
        numbers[entity] = 11;
        names[entity] = "e10";

        table.Release(entity);

        released.Should().Equal($"numbers {entity.Index}: 11", $"names {entity.Index}: e10");
    }

    [Fact]
    public void AReleasedSlotKeepsNothingInAnyColumnAndItsSuccessorStartsClear()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        EntityTable.Column<string?> names = table.AddColumn<string?>();
        EntityTable.Column<long> silent = table.AddColumn<long>();
        Entity entity = table.SpawnLive();
        numbers[entity] = 11;
        names[entity] = "e10";
        silent[entity] = -1;

        table.Release(entity);

        numbers.AsSpan()[(int)entity.Index].Should().Be(0);
        names.AsSpan()[(int)entity.Index].Should().BeNull();
        silent.AsSpan()[(int)entity.Index].Should().Be(0);
        table.AssertInvariants();
        Entity successor = table.SpawnLive();
        successor.Index.Should().Be(entity.Index);
        numbers[successor].Should().Be(0);
        names[successor].Should().BeNull();
    }

    [Fact]
    public void AColumnAddedAfterEntitiesExistCoversEveryExistingSlot()
    {
        EntityTable table = TableSupport.NewTable();
        Entity[] existing = [.. Enumerable.Range(0, 10).Select(_ => table.SpawnLive())];

        EntityTable.Column<int> numbers = table.AddColumn<int>();

        numbers.AsSpan().Length.Should().Be(table.Capacity);
        existing.Select(e => numbers[e]).Should().OnlyContain(n => n == 0);
        table.AssertInvariants();
    }

    [Fact]
    public void AComponentChangeKeepsTheMaskInStep()
    {
        EntityTable table = TableSupport.NewTable();
        var pings = new Recorder(EventMask.Of<Ping>());
        Entity entity = table.SpawnLiveWith(null, pings);
        table.Accepts(entity, EventType<Ping>.Id).Should().BeTrue();
        table.Accepts(entity, EventType<Heal>.Id).Should().BeFalse();
        // Every mask has Destroy, so that a Destroy always gets through.
        table.Accepts(entity, EventType<Destroy>.Id).Should().BeTrue();

        var heals = new Recorder(EventMask.Of<Heal>(), priority: -1);
        table.AddComponent(entity, heals);
        table.Components(entity).ToArray().Should().Equal(heals, pings);
        table.ComponentWants(entity).ToArray().Should().Equal(heals.Wants, pings.Wants);
        table.Accepts(entity, EventType<Heal>.Id).Should().BeTrue();
        Action addAgain = () => table.AddComponent(entity, heals);
        addAgain.Should().Throw<InvalidOperationException>();
        table.AssertInvariants();

        table.RemoveComponent(entity, pings).Should().BeTrue();
        table.RemoveComponent(entity, pings).Should().BeFalse();
        table.Accepts(entity, EventType<Ping>.Id).Should().BeFalse();
        table.Accepts(entity, EventType<Heal>.Id).Should().BeTrue();
        table.AssertInvariants();
    }

    [Fact]
    public void ARemovedComponentsWantsStillLeaveDestroyInTheMask()
    {
        EntityTable table = TableSupport.NewTable();
        var only = new Recorder(EventMask.Of<Ping>());
        Entity entity = table.SpawnLiveWith(null, only);

        table.RemoveComponent(entity, only);

        table.Accepts(entity, EventType<Destroy>.Id).Should().BeTrue();
        table.Accepts(entity, EventType<Ping>.Id).Should().BeFalse();
        table.AssertInvariants();
    }

    [Fact]
    public void AComponentWithTheSamePriorityRunsAfterTheOnesAlreadyThere()
    {
        EntityTable table = TableSupport.NewTable();
        var first = new Recorder(EventMask.Of<Ping>(), priority: 3);
        var last = new Recorder(EventMask.Of<Ping>(), priority: 9);
        Entity entity = table.SpawnLiveWith(null, first, last);

        var tie = new Recorder(EventMask.Of<Ping>(), priority: 3);
        table.AddComponent(entity, tie);

        table.Components(entity).ToArray().Should().Equal(first, tie, last);
    }

    [Fact]
    public void ASyncWriteIsCopiedForwardOnlyWhenItChangesTheRow()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.SpawnLive();
        table.TakeMoved().ToArray().Should().Equal(entity);
        table.TakeMoved().IsEmpty.Should().BeTrue();

        table.SetBackTransform(entity, RowAt(5));
        table.BackRow(entity).Pose.Position.X.Should().Be(5);
        table.FrontRow(entity).Pose.Position.X.Should().Be(0);
        table.AssertInvariants();

        table.CopyDirtyRows();
        table.FrontRow(entity).Pose.Position.X.Should().Be(5);
        table.TakeMoved().ToArray().Should().Equal(entity);
        table.AssertInvariants();

        // The same row again changes nothing, so the entity does not move.
        table.SetBackTransform(entity, RowAt(5));
        table.CopyDirtyRows();
        table.TakeMoved().IsEmpty.Should().BeTrue();
        table.AssertInvariants();
    }

    [Fact]
    public void AGameplayWriteIsCopiedForwardAtTheSyncAndForgottenAfterIt()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.SpawnLive();
        table.TakeMoved();

        table.BeginTick(spawnPoolSize: 0);
        table.TakeFirstTick(entity).Should().BeTrue();
        table.TakeFirstTick(entity).Should().BeFalse();
        table.WritableTransform(entity) = TestWorld.At(3);
        table.WritableRender(entity).Scale = new(2);
        table.FrontRow(entity).Pose.Position.X.Should().Be(0);
        table.EndGameplay().Should().Be(0);
        table.GameplayWroteTransform(entity).Should().BeTrue();
        table.AssertInvariants();

        table.CopyDirtyRows();
        table.EndTick();
        table.FrontRow(entity).Pose.Position.X.Should().Be(3);
        table.FrontRender(entity).Scale.Should().Be(new Vector3(2));
        table.GameplayWroteTransform(entity).Should().BeFalse();
        table.TakeMoved().ToArray().Should().Equal(entity);
        table.AssertInvariants();
    }

    [Fact]
    public void AnEntityRunsItsFirstTickOnlyOnceAfterItBecomesLive()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.SpawnLive();

        table.BeginTick(spawnPoolSize: 0);
        bool onFirstRun = table.TakeFirstTick(entity);
        table.EndGameplay();
        table.EndTick();
        table.BeginTick(spawnPoolSize: 0);
        bool onSecondRun = table.TakeFirstTick(entity);
        table.EndGameplay();
        table.EndTick();

        (onFirstRun, onSecondRun).Should().Be((true, false));
    }

    [Fact]
    public void TheIntentListHoldsEntitiesInTheOrderTheyFirstWroteTheirIntents()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        Entity b = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(b).Force = new(2, 0, 0);
        table.WritableIntent(a).Force = new(1, 0, 0);
        table.WritableIntent(b).Force += new Vector3(0, 5, 0);
        table.PendingIntent(a).Force.Should().Be(new Vector3(1, 0, 0));
        table.EndGameplay();

        table.SwapIntents(keep: true);
        table.EndTick();

        table.IntentEntities.ToArray().Should().Equal(b, a);
        table.FrontIntents.Span[(int)a.Index].Force.Should().Be(new Vector3(1, 0, 0));
        table.FrontIntents.Span[(int)b.Index].Force.Should().Be(new Vector3(2, 5, 0));
        table.PendingIntent(a).Should().Be(default(PhysicsIntent));
        table.AssertInvariants();
    }

    [Fact]
    public void TheIntentListDropsEntitiesThatDiedAfterTheSwap()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        Entity b = table.SpawnLiveWith(TestWorld.Ball);
        WriteIntents(table, a, b);

        table.Release(a);

        table.IntentEntities.ToArray().Should().Equal(b);
        table.FrontIntents.Span[(int)a.Index].Should().Be(default(PhysicsIntent));
        table.AssertInvariants();
    }

    [Fact]
    public void ASwapWithNothingWrittenClearsTheListAndTheIntentsItNamed()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        WriteIntents(table, a);

        table.BeginTick(spawnPoolSize: 0);
        table.EndGameplay();
        table.SwapIntents(keep: true);

        table.IntentEntities.IsEmpty.Should().BeTrue();
        table.FrontIntents.Span[(int)a.Index].Should().Be(default(PhysicsIntent));
        table.AssertInvariants();
    }

    [Fact]
    public void WithoutAPhysicsBackendTheSwapDropsTheIntents()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(a).Force = new(1, 0, 0);
        table.EndGameplay();

        table.SwapIntents(keep: false);

        table.IntentEntities.IsEmpty.Should().BeTrue();
        table.PendingIntent(a).Should().Be(default(PhysicsIntent));
        table.AssertInvariants();
    }

    [Fact]
    public void AnEntityThatDiedAfterWritingAnIntentIsNotKeptByTheSwap()
    {
        EntityTable table = TableSupport.NewTable();
        Entity doomed = table.SpawnLiveWith(TestWorld.Ball);
        Entity survivor = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(doomed).Force = new(1, 0, 0);
        table.WritableIntent(survivor).Force = new(2, 0, 0);
        table.EndGameplay();
        table.Release(doomed);

        table.SwapIntents(keep: true);

        table.IntentEntities.ToArray().Should().Equal(survivor);
        table.FrontIntents.Span[(int)doomed.Index].Should().Be(default(PhysicsIntent));
        table.AssertInvariants();
    }

    // Not reachable through the world today: its sync point swaps the intents (step 4) before it destroys entities (step
    // 5) and makes new ones live (step 6). The table's own interface allows the order, so the table should survive it.
    [Fact]
    public void AnIntentWrittenByAnEntityThatDiedIsNotGivenToTheEntityThatTookItsSlot()
    {
        EntityTable table = TableSupport.NewTable();
        Entity writer = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(writer).Force = new(1, 0, 0);
        table.EndGameplay();
        table.Release(writer);
        Entity successor = table.SpawnLiveWith(TestWorld.Ball);
        successor.Index.Should().Be(writer.Index);

        table.SwapIntents(keep: true);

        // The successor wrote no intent, so physics must not be told that it did.
        table.IntentEntities.IsEmpty.Should().BeTrue();
        table.FrontIntents.Span[(int)successor.Index].Should().Be(default(PhysicsIntent));
        table.AssertInvariants();
    }

    [Fact]
    public void AnIntentWriterThatDiesBeforeTheSwapBreaksNoInvariantWhileItIsStillListed()
    {
        EntityTable table = TableSupport.NewTable();
        Entity writer = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(writer).Force = new(1, 0, 0);
        table.EndGameplay();

        table.Release(writer);
        table.AssertInvariants();
        table.SpawnLiveWith(TestWorld.Ball).Index.Should().Be(writer.Index);
        table.AssertInvariants();
    }

    [Fact]
    public void AChunkWithNoLiveEntityReturnsItsPoolAndDoesNotRun()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLive();
        table.BeginTick(spawnPoolSize: 1);
        table.LiveEntitiesOfChunk(0).ToList().Should().Equal(a);
        table.EndGameplay();
        table.EndTick();

        table.Release(a);
        table.BeginTick(spawnPoolSize: 1);
        table.LiveEntitiesOfChunk(0).ToList().Should().BeEmpty();
        table.EndGameplay();
        table.EndTick();

        table.AssertInvariants();
        // Both slots are free again: the released one and the pool slot the chunk gave back.
        table.Reserve().Should().NotBe(a);
        table.Reserve();
        table.SlotCount.Should().Be(2);
    }

    [Fact]
    public void AGenerationThatWrapsAroundSkipsZeroBecauseZeroMeansNoEntity()
    {
        // A generation has 32 bits, so reaching the end by releasing one slot takes four billion releases. The test puts
        // the slot's generation at the last value instead, through the table's private array, and then releases it once.
        EntityTable table = TableSupport.NewTable();
        Entity first = table.SpawnLive();
        SetGeneration(table, first.Index, uint.MaxValue);
        var last = new Entity(first.Index, uint.MaxValue);
        table.IsAlive(last).Should().BeTrue();

        table.Release(last).Should().BeTrue();
        Entity wrapped = table.Reserve();

        wrapped.Index.Should().Be(first.Index);
        wrapped.Generation.Should().Be(1u);
        wrapped.IsNull.Should().BeFalse();
        table.IsAlive(last).Should().BeFalse();
        table.AssertInvariants();
    }

    private static void SetGeneration(EntityTable table, uint slot, uint generation)
    {
        uint[] generations =
            (uint[])typeof(EntityTable).GetField("generation", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(table)!;
        generations[slot] = generation;
    }

    [Fact]
    public void ALiveRangeStopsAtTheSlotsThatWereEverHandedOut()
    {
        EntityTable table = TableSupport.NewTable();
        Entity[] entities = [table.SpawnLive(), table.SpawnLive(), table.SpawnLive()];

        // Slots 1 and 2 are live, and the range asks for far more than the three slots there are.
        table.LiveEntitiesIn(1, 1000).ToList().Should().Equal(entities[1], entities[2]);
    }

    [Fact]
    public void ALiveRangeThatStartsPastTheLastSlotIsEmpty()
    {
        EntityTable table = TableSupport.NewTable();
        table.SpawnLive();

        table.LiveEntitiesIn(5, 3).ToList().Should().BeEmpty();
    }

    [Fact]
    public void ALiveRangeOfZeroSlotsIsEmpty()
    {
        EntityTable table = TableSupport.NewTable();
        table.SpawnLive();

        table.LiveEntitiesIn(0, 0).ToList().Should().BeEmpty();
    }

    [Fact]
    public void TheChunksCoverExactlyTheSlotsHandedOut()
    {
        EntityTable table = TableSupport.NewTable();
        table.Chunks.Length.Should().Be(0);

        for (int i = 0; i < 9; i++)
            table.SpawnLive();

        // Nine slots in chunks of four: three chunks, the last one part full.
        table.Chunks.Length.Should().Be(3);
    }

    /// <summary>One tick in which the given entities write an intent, then the swap that makes the intents the physics input.</summary>
    private static void WriteIntents(EntityTable table, params Entity[] writers)
    {
        table.BeginTick(spawnPoolSize: 0);
        foreach (Entity writer in writers)
            table.WritableIntent(writer).Force = new(1, 0, 0);
        table.EndGameplay();
        table.SwapIntents(keep: true);
        table.EndTick();
    }

#if TERTIUS_CONTRACTS
    [Fact]
    public void LifecycleStepsOutOfOrderBreakAContract()
    {
        EntityTable table = TableSupport.NewTable();
        Entity live = table.SpawnLive();
        Entity reserved = table.Reserve();

        table.BeginTick(spawnPoolSize: 0);
        ((Action)(() => table.Reserve())).Should().Throw<ContractException>();
        ((Action)(() => table.Release(reserved))).Should().Throw<ContractException>();
        ((Action)(() => table.SetBackTransform(live, RowAt(1)))).Should().Throw<ContractException>();
        ((Action)(() => table.WritableIntent(live))).Should().Throw<ContractException>();
        table.EndGameplay();
        ((Action)(() => table.WritableTransform(live))).Should().Throw<ContractException>();
    }

    // The world swaps the intents before it ends the tick. Ending it first would drop the writer from its chunk's list
    // while its slot still says that it is listed, so its later intents would never reach physics.
    [Fact]
    public void EndingATickBeforeItsIntentsAreSwappedBreaksAContract()
    {
        EntityTable table = TableSupport.NewTable();
        Entity writer = table.SpawnLiveWith(TestWorld.Ball);
        table.BeginTick(spawnPoolSize: 0);
        table.WritableIntent(writer).Force = new(1, 0, 0);
        table.EndGameplay();

        Action endTick = table.EndTick;

        endTick.Should().Throw<ContractException>();
    }
#endif
}
