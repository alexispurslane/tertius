using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.EntityTableSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The dirty-row copy. A row is dirty when the back copy may differ from the front copy. <c>CopyDirtyRows</c> copies the
/// dirty rows forward and nothing else: an entity that nobody wrote keeps its front rows and is not reported as moved.
/// The table only ever reads the rows that gameplay or the sync point marked dirty, so these tests check, for each kind
/// of write, which rows changed and which entities were reported as moved.
/// </summary>
public class EntityTableDirtyRowTests
{
    private const int ChunkSize = 4;

    // ---- What counts as a change -----------------------------------------------------------------------------------
    // A write of the sync point marks the row dirty only if the new row differs from the back row bit for bit. The
    // comparison covers the whole row, and tells 0 from -0 and a NaN from itself, so that the front row is always exactly
    // what the back row is.

    private static readonly FrameHandle SomeFrame = new(new(5, 1), 2, 1);

    /// <summary>
    /// A new table with chunks of <see cref="ChunkSize"/> slots, which copies on one thread.
    /// <see cref="EntityTableParallelCopyTests"/> runs every test of this class again on a table that copies on the
    /// workers.
    /// </summary>
    private protected virtual EntityTable NewTable() => TableSupport.NewTable();

    /// <summary>A table with live entities in slots 0 to <paramref name="count"/> - 1, whose "moved" list is already read.</summary>
    private (EntityTable Table, Entity[] Entities) Settled(int count)
    {
        EntityTable table = NewTable();
        Entity[] entities = [.. Enumerable.Range(0, count).Select(i => table.SpawnLiveAt(i))];
        table.TakeMoved();
        return (table, entities);
    }

    /// <summary>One gameplay part of a tick, in which <paramref name="writes"/> runs, followed by the end of that part.</summary>
    private static void Gameplay(EntityTable table, Action writes)
    {
        table.BeginTick(spawnPoolSize: 0);
        writes();
        table.EndGameplay();
    }

    [Fact]
    public void ATransformWriteIsCopiedForwardWithoutTouchingTheRenderRow()
    {
        (EntityTable table, Entity[] e) = Settled(1);
        Gameplay(table, () => table.WritableTransform(e[0]) = TestWorld.At(6));

        table.CopyDirtyRows();

        table.FrontRow(e[0]).Pose.Position.X.Should().Be(6);
        table.FrontRender(e[0]).Scale.Should().Be(new RenderData().Scale);
        table.AssertInvariants();
    }

    [Fact]
    public void ARenderWriteIsCopiedForwardWithoutTouchingTheTransformRow()
    {
        (EntityTable table, Entity[] e) = Settled(1);
        Gameplay(table, () => table.WritableRender(e[0]).Scale = new(3));

        table.CopyDirtyRows();

        table.FrontRender(e[0]).Scale.Should().Be(new Vector3(3));
        table.FrontRow(e[0]).Pose.Position.X.Should().Be(0);
        table.AssertInvariants();
    }

    [Fact]
    public void ARenderWriteAloneDoesNotMakeTheEntityMoved()
    {
        (EntityTable table, Entity[] e) = Settled(1);
        Gameplay(table, () => table.WritableRender(e[0]).Scale = new(3));

        table.CopyDirtyRows();

        table.TakeMoved().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AnEntityWithBothRowsWrittenGetsBothCopiedForward()
    {
        (EntityTable table, Entity[] e) = Settled(1);
        Gameplay(
            table,
            () =>
            {
                table.WritableRender(e[0]).Scale = new(3);
                table.WritableTransform(e[0]) = TestWorld.At(6);
            }
        );

        table.CopyDirtyRows();

        table.FrontRender(e[0]).Scale.Should().Be(new Vector3(3));
        table.FrontRow(e[0]).Pose.Position.X.Should().Be(6);
        table.TakeMoved().ToArray().Should().Equal(e[0]);
        table.AssertInvariants();
    }

    [Fact]
    public void EntitiesThatNobodyWroteKeepTheirRowsAndAreNotMoved()
    {
        (EntityTable table, Entity[] e) = Settled(3);
        Gameplay(table, () => table.WritableTransform(e[1]) = TestWorld.At(50));

        table.CopyDirtyRows();

        table.FrontRow(e[0]).Pose.Position.X.Should().Be(0);
        table.FrontRow(e[2]).Pose.Position.X.Should().Be(2);
        table.TakeMoved().ToArray().Should().Equal(e[1]);
    }

    [Fact]
    public void ACopyWithNothingNewToCopyChangesNothingAndMovesNobody()
    {
        (EntityTable table, Entity[] e) = Settled(2);
        Gameplay(table, () => table.WritableTransform(e[0]) = TestWorld.At(9));
        table.CopyDirtyRows();
        table.TakeMoved();

        table.CopyDirtyRows();

        table.FrontRow(e[0]).Pose.Position.X.Should().Be(9);
        table.TakeMoved().IsEmpty.Should().BeTrue();
        table.AssertInvariants();
    }

    [Fact]
    public void AnEntityWrittenTwiceInOneTickIsCopiedAndListedAsMovedOnce()
    {
        (EntityTable table, Entity[] e) = Settled(1);
        Gameplay(
            table,
            () =>
            {
                table.WritableTransform(e[0]) = TestWorld.At(4);
                table.WritableTransform(e[0]) = TestWorld.At(8);
            }
        );

        table.CopyDirtyRows();

        table.FrontRow(e[0]).Pose.Position.X.Should().Be(8);
        table.TakeMoved().ToArray().Should().Equal(e[0]);
    }

    [Fact]
    public void WhenGameplayAndTheSyncPointWriteTheSameRowTheSyncPointsValueBecomesVisible()
    {
        (EntityTable table, Entity[] e) = Settled(1);
        Gameplay(table, () => table.WritableTransform(e[0]) = TestWorld.At(3));

        table.SetBackTransform(e[0], RowAt(8));
        table.CopyDirtyRows();

        table.FrontRow(e[0]).Pose.Position.X.Should().Be(8);
        table.TakeMoved().ToArray().Should().Equal(e[0]);
        table.AssertInvariants();
    }

    [Fact]
    public void MovedEntitiesAreListedInChunkOrderWhicheverChunkWroteFirst()
    {
        // Entities in slots 0, 4 and 8 are in chunks 0, 1 and 2. Each chunk's worker writes its own entity; the workers
        // may finish in any order, but the result must not show it.
        (EntityTable table, Entity[] e) = Settled(2 * ChunkSize + 1);
        Entity inChunk0 = e[0],
            inChunk1 = e[ChunkSize],
            inChunk2 = e[2 * ChunkSize];
        Gameplay(
            table,
            () =>
            {
                table.WritableTransform(inChunk2) = TestWorld.At(20);
                table.WritableTransform(inChunk0) = TestWorld.At(21);
                table.WritableTransform(inChunk1) = TestWorld.At(22);
            }
        );

        table.CopyDirtyRows();

        table.TakeMoved().ToArray().Should().Equal(inChunk0, inChunk1, inChunk2);
    }

    [Fact]
    public void RowsWrittenByGameplayAreCopiedBeforeRowsWrittenBySyncPoint()
    {
        (EntityTable table, Entity[] e) = Settled(2 * ChunkSize);
        Entity syncWritten = e[1],
            gameplayWritten = e[ChunkSize];
        Gameplay(table, () => table.WritableTransform(gameplayWritten) = TestWorld.At(30));
        table.SetBackTransform(syncWritten, RowAt(31));

        table.CopyDirtyRows();

        table.TakeMoved().ToArray().Should().Equal(gameplayWritten, syncWritten);
    }

    [Fact]
    public void ARowWrittenByGameplayBeforeTheEntityDiedIsNotCopiedIntoTheEntityThatReusesTheSlot()
    {
        (EntityTable table, Entity[] e) = Settled(1);
        Gameplay(table, () => table.WritableTransform(e[0]) = TestWorld.At(9));
        table.Release(e[0]);
        Entity successor = table.SpawnLiveAt(1);
        successor.Index.Should().Be(e[0].Index);

        table.CopyDirtyRows();

        table.FrontRow(successor).Pose.Position.X.Should().Be(1);
        table.TakeMoved().ToArray().Should().Equal(successor);
        table.AssertInvariants();
    }

    [Fact]
    public void ARowWrittenBySyncBeforeTheEntityDiedIsNotCopiedIntoTheEntityThatReusesTheSlot()
    {
        (EntityTable table, Entity[] e) = Settled(1);
        table.SetBackTransform(e[0], RowAt(9));
        table.Release(e[0]);
        Entity successor = table.SpawnLiveAt(1);
        successor.Index.Should().Be(e[0].Index);

        table.CopyDirtyRows();

        table.FrontRow(successor).Pose.Position.X.Should().Be(1);
        table.TakeMoved().ToArray().Should().Equal(successor);
        table.AssertInvariants();
    }

    [Fact]
    public void ADeadEntityWithAWrittenRowIsNeverReportedAsMoved()
    {
        (EntityTable table, Entity[] e) = Settled(2);
        Gameplay(table, () => table.WritableTransform(e[0]) = TestWorld.At(9));
        table.Release(e[0]);

        table.CopyDirtyRows();

        table.TakeMoved().IsEmpty.Should().BeTrue();
        table.AssertInvariants();
    }

    [Fact]
    public void ANewEntityCountsAsMovedOnceItIsLive()
    {
        EntityTable table = NewTable();
        Entity first = table.SpawnLiveAt(1);
        table.TakeMoved();

        Entity second = table.SpawnLiveAt(2);

        table.TakeMoved().ToArray().Should().Equal(second);
        table.TakeMoved().IsEmpty.Should().BeTrue();
        first.Should().NotBe(second);
    }

    /// <summary>A transform row at x = 1, in no frame, with a unit rotation and some velocities, so that every field has a value.</summary>
    private static TransformRow BaseRow() =>
        new()
        {
            Pose = TestWorld.At(1),
            LinearVelocity = new(1, 2, 3),
            AngularVelocity = new(4, 5, 6),
        };

    private static TransformRow ChangedIn(string field) =>
        field switch
        {
            "frame" => BaseRow() with { Frame = SomeFrame },
            "position" => BaseRow() with { Pose = TestWorld.At(2) },
            "rotation" => BaseRow() with
            {
                Pose = BaseRow().Pose with { Rotation = DQuaternion.FromAxisAngle(Double3.UnitY, 0.5) },
            },
            "linear velocity" => BaseRow() with { LinearVelocity = new(1, 2, 4) },
            "angular velocity" => BaseRow() with { AngularVelocity = new(4, 5, 7) },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    [Theory]
    [InlineData("frame")]
    [InlineData("position")]
    [InlineData("rotation")]
    [InlineData("linear velocity")]
    [InlineData("angular velocity")]
    public void AChangeInAnyFieldOfTheRowIsCopiedForward(string field)
    {
        EntityTable table = NewTable();
        Entity entity = table.SpawnLive();
        table.SetBackTransform(entity, BaseRow());
        table.CopyDirtyRows();
        table.TakeMoved();
        TransformRow changed = ChangedIn(field);

        table.SetBackTransform(entity, changed);
        table.CopyDirtyRows();

        table.FrontRow(entity).Frame.Should().Be(changed.Frame);
        table.FrontRow(entity).Pose.Should().Be(changed.Pose);
        table.FrontRow(entity).LinearVelocity.Should().Be(changed.LinearVelocity);
        table.FrontRow(entity).AngularVelocity.Should().Be(changed.AngularVelocity);
        table.TakeMoved().ToArray().Should().Equal(entity);
    }

    [Fact]
    public void ARowThatIsIdenticalInEveryFieldIsNotCopiedAndMovesNobody()
    {
        EntityTable table = NewTable();
        Entity entity = table.SpawnLive();
        table.SetBackTransform(entity, BaseRow());
        table.CopyDirtyRows();
        table.TakeMoved();

        table.SetBackTransform(entity, BaseRow());
        table.CopyDirtyRows();

        table.TakeMoved().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ARowThatDiffersOnlyByTheSignOfAZeroIsStillACopiedChange()
    {
        EntityTable table = NewTable();
        Entity entity = table.SpawnLive();
        table.CopyDirtyRows();
        table.TakeMoved();

        table.SetBackTransform(entity, new() { Pose = new(new(-0.0, 0, 0), DQuaternion.Identity) });
        table.CopyDirtyRows();

        double.IsNegative(table.FrontRow(entity).Pose.Position.X).Should().BeTrue();
        table.TakeMoved().ToArray().Should().Equal(entity);
    }

    [Fact]
    public void ARowWithANotANumberIsNotAChangeWhenItsWrittenAgainBitForBit()
    {
        EntityTable table = NewTable();
        Entity entity = table.SpawnLive();
        var broken = new TransformRow { Pose = new(new(double.NaN, 0, 0), DQuaternion.Identity) };
        table.SetBackTransform(entity, broken);
        table.CopyDirtyRows();
        table.TakeMoved();

        table.SetBackTransform(entity, broken);
        table.CopyDirtyRows();

        // NaN is not equal to itself with ==, so a plain comparison would call this a change on every write.
        table.TakeMoved().IsEmpty.Should().BeTrue();
        table.AssertInvariants();
    }

    [Fact]
    public void ARenderRowWrittenByGameplayAndATransformRowWrittenBySyncAreBothCopied()
    {
        (EntityTable table, Entity[] e) = Settled(1);
        Gameplay(table, () => table.WritableRender(e[0]).Scale = new(3));
        table.SetBackTransform(e[0], RowAt(8));

        table.CopyDirtyRows();

        table.FrontRender(e[0]).Scale.Should().Be(new Vector3(3));
        table.FrontRow(e[0]).Pose.Position.X.Should().Be(8);
        table.TakeMoved().ToArray().Should().Equal(e[0]);
        table.AssertInvariants();
    }
}
