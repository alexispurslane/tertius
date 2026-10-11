using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Entity tables in which a handle has gone stale. The dangerous case is a reused slot: the stale handle's index names
/// a live entity, just not the one the handle was made for. Released slots come back first in, first out, so a table
/// with no other free slot hands the released one to the next spawn.
/// </summary>
internal static class StaleTables
{
    /// <summary>
    /// Releases a live entity and spawns <paramref name="successor"/>, which takes the released slot. Returns the
    /// released entity's handle, which is now stale.
    /// </summary>
    public static Entity ReuseSlot(this EntityTable table, out Entity successor, BodyDesc? body = null, params IComponent[] components)
    {
        Entity stale = table.SpawnLiveWith(body);
        table.Release(stale);
        successor = table.SpawnLiveWith(body, components);
        successor.Index.Should().Be(stale.Index);
        return stale;
    }
}

/// <summary>What the entity table does with a stale handle, member by member.</summary>
public class StaleHandleTests
{
    private static TransformRow RowAt(double x) => new() { Pose = TestWorld.At(x) };

    /// <summary>One read of an entity's data through the table.</summary>
    private static Action Read(EntityTable table, string member, Entity entity) => member switch
    {
        nameof(EntityTable.FrontRow) => () => _ = table.FrontRow(entity).Pose,
        nameof(EntityTable.BackRow) => () => _ = table.BackRow(entity).Pose,
        nameof(EntityTable.FrontRender) => () => _ = table.FrontRender(entity).Scale,
        nameof(EntityTable.Components) => () => _ = table.Components(entity).Length,
        nameof(EntityTable.ComponentWants) => () => _ = table.ComponentWants(entity).Length,
        nameof(EntityTable.Body) => () => _ = table.Body(entity),
        nameof(EntityTable.OwnedFrame) => () => _ = table.OwnedFrame(entity),
        nameof(EntityTable.Accepts) => () => _ = table.Accepts(entity, EventType<Destroy>.Id),
        nameof(EntityTable.PendingIntent) => () => _ = table.PendingIntent(entity),
        nameof(EntityTable.GameplayWroteTransform) => () => _ = table.GameplayWroteTransform(entity),
        nameof(EntityTable.TakeFirstTick) => () => _ = table.TakeFirstTick(entity),
        _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
    };

    [Theory]
    [InlineData(nameof(EntityTable.FrontRow))]
    [InlineData(nameof(EntityTable.BackRow))]
    [InlineData(nameof(EntityTable.FrontRender))]
    [InlineData(nameof(EntityTable.Components))]
    [InlineData(nameof(EntityTable.ComponentWants))]
    [InlineData(nameof(EntityTable.Body))]
    [InlineData(nameof(EntityTable.OwnedFrame))]
    [InlineData(nameof(EntityTable.Accepts))]
    [InlineData(nameof(EntityTable.PendingIntent))]
    [InlineData(nameof(EntityTable.GameplayWroteTransform))]
    [InlineData(nameof(EntityTable.TakeFirstTick))]
    public void AHandleToAReusedSlotCannotReadTheNewEntitysData(string member)
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out Entity successor);

        Read(table, member, stale).Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        Read(table, member, successor).Should().NotThrow();
    }

    [Theory]
    [InlineData(nameof(EntityTable.WritableTransform))]
    [InlineData(nameof(EntityTable.WritableRender))]
    [InlineData(nameof(EntityTable.WritableIntent))]
    public void AHandleToAReusedSlotCannotWriteTheNewEntitysRowsDuringATick(string member)
    {
        EntityTable table = TableSupport.NewTable();
        // An intent needs a body; a gameplay-written transform needs none.
        BodyDesc? body = member == nameof(EntityTable.WritableIntent) ? TestWorld.Ball : null;
        Entity stale = table.ReuseSlot(out Entity successor, body);
        table.BeginTick(spawnPoolSize: 0);

        Action write = member switch
        {
            nameof(EntityTable.WritableTransform) => () => table.WritableTransform(stale) = TestWorld.At(9),
            nameof(EntityTable.WritableRender) => () => table.WritableRender(stale).Scale = new Vector3(9),
            _ => () => table.WritableIntent(stale).Force = new Vector3(9, 0, 0),
        };
        write.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);

        table.EndGameplay();
        table.GameplayWroteTransform(successor).Should().BeFalse();
        table.PendingIntent(successor).Should().Be(default(PhysicsIntent));
    }

    [Fact]
    public void AStaleHandleCannotWriteAReleasedEntitysTransform()
    {
        EntityTable table = TableSupport.NewTable();
        Entity released = table.SpawnLive();
        table.Release(released);

        Action write = () => table.SetBackTransform(released, RowAt(5));

        write.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(released);
    }

    [Fact]
    public void AStaleHandleCannotGiveTheEntityThatReusedItsSlotAComponent()
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out Entity successor);

        Action add = () => table.AddComponent(stale, new Recorder());

        add.Should().Throw<StaleEntityException>();
        table.Components(successor).Length.Should().Be(0);
    }

    [Fact]
    public void RemovingAComponentThroughAStaleHandleReturnsFalseAndLeavesTheSuccessorsComponent()
    {
        EntityTable table = TableSupport.NewTable();
        var kept = new Recorder();
        Entity stale = table.ReuseSlot(out Entity successor, components: kept);

        table.RemoveComponent(stale, kept).Should().BeFalse();

        table.Components(successor).ToArray().Should().Equal(kept);
    }

    [Fact]
    public void ReleasingAnEntityTwiceReturnsFalseTheSecondTime()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.SpawnLive();

        table.Release(entity).Should().BeTrue();
        table.Release(entity).Should().BeFalse();

        table.LiveCount.Should().Be(0);
        table.AssertInvariants();
    }

    [Fact]
    public void ReleasingAStaleHandleLeavesTheEntityThatReusedItsSlotLive()
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out Entity successor);

        table.Release(stale).Should().BeFalse();

        table.IsAlive(successor).Should().BeTrue();
        table.LiveCount.Should().Be(1);
        table.AssertInvariants();
    }

    [Fact]
    public void AReservationIsReleasedOnceThroughItsOwnHandle()
    {
        EntityTable table = TableSupport.NewTable();
        Entity reservation = table.Reserve();

        table.Release(reservation).Should().BeTrue();
        table.Release(reservation).Should().BeFalse();

        table.AssertInvariants();
    }

    [Fact]
    public void AnEndedReservationCannotBeActivated()
    {
        EntityTable table = TableSupport.NewTable();
        Entity reservation = table.Reserve();
        table.Release(reservation);
        Entity next = table.Reserve();

        Action activate = () => table.Activate(reservation, default, new RenderData(), [], [], null, FrameHandle.Null);

        activate.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(reservation);
        table.IsAlive(next).Should().BeFalse();
    }

    // Not a contract: the check must hold in Release too, where a second activation would count the entity twice.
    [Fact]
    public void ALiveEntityCannotBeActivatedAgain()
    {
        EntityTable table = TableSupport.NewTable();
        Entity live = table.SpawnLive();

        Action activate = () => table.Activate(live, RowAt(9), new RenderData(), [], [], null, FrameHandle.Null);

        activate.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(live);
        table.LiveCount.Should().Be(1);
        table.AssertInvariants();
    }

    [Fact]
    public void AStaleSpawnerCannotReserveASlotFromTheSuccessorsPool()
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out _);
        table.BeginTick(spawnPoolSize: 2);

        Action spawn = () => table.ReserveForSpawn(stale);

        spawn.Should().Throw<StaleEntityException>();
        table.EndGameplay().Should().Be(0);
    }

    [Fact]
    public void TheStaleHandleErrorNamesTheHandle()
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out _);

        Action read = () => _ = table.FrontRow(stale).Pose;

        read.Should().Throw<StaleEntityException>().WithMessage($"*{stale}*");
    }

    [Fact]
    public void TheLiveEntitiesOfAChunkAreTheCurrentHandlesOfItsLiveSlots()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLive(), b = table.SpawnLive(), c = table.SpawnLive();
        table.Release(b);
        Entity reserved = table.Reserve();
        reserved.Index.Should().Be(b.Index);

        Live(table.LiveEntitiesOfChunk(0)).Should().Equal(a, c);
        Live(table.LiveEntities).Should().Equal(a, c);

        table.Activate(reserved, default, new RenderData(), [], [], null, FrameHandle.Null);
        Live(table.LiveEntitiesOfChunk(0)).Should().Equal(a, reserved, c);
        Live(table.LiveEntitiesIn(1, 3)).Should().Equal(reserved, c);
    }

    [Fact]
    public void TheMovedEntitiesNeverIncludeADeadEntity()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLive(), b = table.SpawnLive();
        table.TakeMoved();
        table.SetBackTransform(a, RowAt(2));
        table.SetBackTransform(b, RowAt(3));
        table.CopyDirtyRows();
        table.Release(a);
        Entity successor = table.SpawnLive();

        table.TakeMoved().ToArray().Should().Equal(b, successor);
        table.AssertInvariants();
    }

    [Fact]
    public void ReleaseHooksSeeTheEntityWhileItIsStillLive()
    {
        EntityTable table = TableSupport.NewTable();
        var seen = new List<(Entity Entity, bool Alive, int Value)>();
        EntityTable.Column<int> numbers = null!;
        numbers = table.AddColumn<int>(entity => seen.Add((entity, table.IsAlive(entity), numbers[entity])));
        Entity entity = table.SpawnLive();
        numbers[entity] = 7;

        table.Release(entity);

        seen.Should().Equal((entity, true, 7));
    }

    [Fact]
    public void AColumnRefusesAHandleToAReusedSlot()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        Entity stale = table.ReuseSlot(out Entity successor);

        Action read = () => _ = numbers[stale];

        read.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        numbers[successor].Should().Be(0);
    }

    // ---- Every kind of handle, every kind of member ---------------------------------------------------------------
    // A handle names no live entity in six ways. The table has to give the documented answer to all of them, for every
    // member that takes a handle: throw StaleEntityException where it reads or writes the entity's data, answer false where
    // the goal already holds, and never list a dead entity in a query.

    private static readonly string[] NamesNoLiveEntity =
        ["null", "never issued", "from the future", "reserved, not live yet", "released", "released and reused"];

    /// <summary>A table with one live entity in slot 0, and a handle of the named kind.</summary>
    private static (EntityTable Table, Entity Handle) TableWithHandle(string kind)
    {
        EntityTable table = TableSupport.NewTable();
        Entity bystander = table.SpawnLive();
        Entity handle = kind switch
        {
            "null" => Entity.Null,
            "never issued" => new Entity(100_000, 1),
            // The slot is live, but its generation is lower than the handle's.
            "from the future" => new Entity(bystander.Index, bystander.Generation + 5),
            "reserved, not live yet" => table.Reserve(),
            "released" => ReleasedHandle(table),
            "released and reused" => table.ReuseSlot(out _),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return (table, handle);
    }

    private static Entity ReleasedHandle(EntityTable table)
    {
        Entity entity = table.SpawnLive();
        table.Release(entity);
        return entity;
    }

    /// <summary>The members that throw for a handle that names no live entity, and can be called outside a tick.</summary>
    private static readonly Dictionary<string, Action<EntityTable, Entity>> ThrowOutsideATick = new()
    {
        [nameof(EntityTable.FrontRow)] = (table, entity) => _ = table.FrontRow(entity).Pose,
        [nameof(EntityTable.BackRow)] = (table, entity) => _ = table.BackRow(entity).Pose,
        [nameof(EntityTable.FrontRender)] = (table, entity) => _ = table.FrontRender(entity).Scale,
        [nameof(EntityTable.Components)] = (table, entity) => _ = table.Components(entity).Length,
        [nameof(EntityTable.ComponentWants)] = (table, entity) => _ = table.ComponentWants(entity).Length,
        [nameof(EntityTable.Body)] = (table, entity) => _ = table.Body(entity),
        [nameof(EntityTable.OwnedFrame)] = (table, entity) => _ = table.OwnedFrame(entity),
        [nameof(EntityTable.Accepts)] = (table, entity) => _ = table.Accepts(entity, EventType<Destroy>.Id),
        [nameof(EntityTable.PendingIntent)] = (table, entity) => _ = table.PendingIntent(entity),
        [nameof(EntityTable.GameplayWroteTransform)] = (table, entity) => _ = table.GameplayWroteTransform(entity),
        [nameof(EntityTable.TakeFirstTick)] = (table, entity) => _ = table.TakeFirstTick(entity),
        [nameof(EntityTable.SetBackTransform)] = (table, entity) => table.SetBackTransform(entity, RowAt(5)),
        [nameof(EntityTable.AddComponent)] = (table, entity) => table.AddComponent(entity, new Recorder()),
        ["Column indexer"] = (table, entity) => _ = table.AddColumn<int>()[entity],
    };

    /// <summary>The members that throw for a handle that names no live entity, and can be called only during the gameplay part of a tick.</summary>
    private static readonly Dictionary<string, Action<EntityTable, Entity>> ThrowDuringATick = new()
    {
        [nameof(EntityTable.WritableTransform)] = (table, entity) => table.WritableTransform(entity) = TestWorld.At(9),
        [nameof(EntityTable.WritableRender)] = (table, entity) => table.WritableRender(entity).Scale = new Vector3(9),
        [nameof(EntityTable.WritableIntent)] = (table, entity) => table.WritableIntent(entity).Force = new Vector3(9, 0, 0),
        [nameof(EntityTable.ReserveForSpawn)] = (table, entity) => _ = table.ReserveForSpawn(entity),
    };

    private static TheoryData<string, string> Cross(IEnumerable<string> members, IEnumerable<string> kinds)
    {
        var data = new TheoryData<string, string>();
        foreach (string member in members)
            foreach (string kind in kinds)
                data.Add(member, kind);
        return data;
    }

    public static TheoryData<string, string> ThrowOutsideATickCases() => Cross(ThrowOutsideATick.Keys, NamesNoLiveEntity);

    public static TheoryData<string, string> ThrowDuringATickCases() => Cross(ThrowDuringATick.Keys, NamesNoLiveEntity);

    [Theory]
    [MemberData(nameof(ThrowOutsideATickCases))]
    public void AMemberThatReadsOrWritesAnEntityRefusesAHandleThatNamesNoLiveEntity(string member, string kind)
    {
        (EntityTable table, Entity handle) = TableWithHandle(kind);

        Action call = () => ThrowOutsideATick[member](table, handle);

        call.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(handle);
        table.AssertInvariants();
    }

    [Theory]
    [MemberData(nameof(ThrowDuringATickCases))]
    public void AMemberThatWritesTheRunningEntityRefusesAHandleThatNamesNoLiveEntity(string member, string kind)
    {
        (EntityTable table, Entity handle) = TableWithHandle(kind);
        table.BeginTick(spawnPoolSize: 1);

        Action call = () => ThrowDuringATick[member](table, handle);

        call.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(handle);
        table.EndGameplay();
        table.AssertInvariants();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("never issued")]
    [InlineData("from the future")]
    [InlineData("released")]
    [InlineData("released and reused")]
    public void ActivatingAHandleThatIsNotACurrentReservationIsRefused(string kind)
    {
        (EntityTable table, Entity handle) = TableWithHandle(kind);

        Action activate = () => table.Activate(handle, default, new RenderData(), [], [], null, FrameHandle.Null);

        activate.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(handle);
        table.AssertInvariants();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("never issued")]
    [InlineData("from the future")]
    [InlineData("reserved, not live yet")]
    [InlineData("released")]
    [InlineData("released and reused")]
    public void NoHandleThatNamesNoLiveEntityIsAlive(string kind)
    {
        (EntityTable table, Entity handle) = TableWithHandle(kind);

        table.IsAlive(handle).Should().BeFalse();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("never issued")]
    [InlineData("from the future")]
    [InlineData("released")]
    [InlineData("released and reused")]
    public void ReleasingAHandleThatNamesNothingToReleaseDoesNothing(string kind)
    {
        (EntityTable table, Entity handle) = TableWithHandle(kind);
        int liveBefore = table.LiveCount;

        bool released = table.Release(handle);

        released.Should().BeFalse();
        table.LiveCount.Should().Be(liveBefore);
        // The invariants include "a free slot is in the free list once", so a slot that was freed twice would show here.
        table.AssertInvariants();
    }

    [Theory]
    [MemberData(nameof(NamesNoLiveEntityData))]
    public void RemovingAComponentThroughAHandleThatNamesNoLiveEntityReturnsFalse(string kind)
    {
        (EntityTable table, Entity handle) = TableWithHandle(kind);

        table.RemoveComponent(handle, new Recorder()).Should().BeFalse();
    }

    public static TheoryData<string> NamesNoLiveEntityData() => new(NamesNoLiveEntity);

    [Fact]
    public void AHandleFromTheFutureDoesNotReleaseTheLiveEntityInItsSlot()
    {
        EntityTable table = TableSupport.NewTable();
        Entity live = table.SpawnLive();
        var future = new Entity(live.Index, live.Generation + 1);

        table.Release(future).Should().BeFalse();

        table.IsAlive(live).Should().BeTrue();
    }

    [Fact]
    public void AColumnRefusesAHandleToAReleasedSlotThatNobodyReused()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        Entity released = table.SpawnLive();
        table.Release(released);

        Action read = () => _ = numbers[released];

        read.Should().Throw<StaleEntityException>();
    }

    [Fact]
    public void AStaleHandleToAReleasedSlotIsStillStaleAfterTheSlotIsReservedAgain()
    {
        EntityTable table = TableSupport.NewTable();
        Entity first = table.SpawnLive();
        table.Release(first);

        Entity second = table.Reserve();

        // The new reservation has the next generation, and neither handle is live until the reservation is activated.
        second.Generation.Should().Be(first.Generation + 1);
        table.IsAlive(first).Should().BeFalse();
        table.IsAlive(second).Should().BeFalse();
    }

    [Fact]
    public void ARefusedHandleLeavesEveryOtherEntitysDataUntouched()
    {
        EntityTable table = TableSupport.NewTable();
        Entity bystander = table.SpawnLive(x: 7);
        Entity stale = table.ReuseSlot(out Entity successor);
        table.TakeMoved();

        Record.Exception(() => table.SetBackTransform(stale, RowAt(99)));
        Record.Exception(() => table.AddComponent(stale, new Recorder()));
        table.Release(stale);
        table.CopyDirtyRows();

        table.FrontRow(bystander).Pose.Position.X.Should().Be(7);
        table.BackRow(successor).Pose.Position.X.Should().Be(0);
        table.Components(successor).Length.Should().Be(0);
        table.TakeMoved().IsEmpty.Should().BeTrue();
        table.AssertInvariants();
    }

    private static List<Entity> Live(EntityTable.LiveEntityEnumerator entities)
    {
        var list = new List<Entity>();
        foreach (Entity entity in entities)
            list.Add(entity);
        return list;
    }
}
