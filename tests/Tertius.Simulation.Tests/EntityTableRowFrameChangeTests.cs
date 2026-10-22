using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The count of the writes of a back row that moved it to another frame (<c>EntityTable.RowFrameChanges</c>). Step 7b of
/// the sync point converts the intents of the entities that the sync point moved, and while the count has not changed
/// since its last pass, no entity is in another frame than the one that its intent names, so the World skips the pass.
/// </summary>
public class EntityTableRowFrameChangeTests
{
    private static readonly FrameHandle Here = new(new(100, 1), 1, 1), There = new(new(101, 1), 2, 1);

    private readonly EntityTable table = TableSupport.NewTable();

    [Fact]
    public void AWriteThatMovesARowToAnotherFrameCounts()
    {
        Entity entity = table.SpawnLive(Here);
        long before = table.RowFrameChanges;

        table.SetBackTransform(entity, table.BackRow(entity) with { Frame = There, PhysicsHome = There });

        table.RowFrameChanges.Should().Be(before + 1);
    }

    [Fact]
    public void AWriteInTheSameFrameDoesNotCount()
    {
        Entity entity = table.SpawnLive(Here);
        long before = table.RowFrameChanges;

        table.SetBackTransform(entity, table.BackRow(entity) with { Pose = TestWorld.At(5) });

        table.RowFrameChanges.Should().Be(before);
    }

    [Fact]
    public void AGameplayWriteOfATransformDoesNotCountBecauseItStaysInItsFrame()
    {
        Entity entity = table.SpawnLive(Here);
        long before = table.RowFrameChanges;

        table.BeginTick(spawnPoolSize: 0);
        table.WritableTransform(entity) = TestWorld.At(5);
        table.EndGameplay();

        table.RowFrameChanges.Should().Be(before);
    }

    [Fact]
    public void ASpawnDoesNotCountBecauseItsIntentNamesTheFrameItStartsIn()
    {
        long before = table.RowFrameChanges;

        table.SpawnLive(There);

        table.RowFrameChanges.Should().Be(before);
    }
}
