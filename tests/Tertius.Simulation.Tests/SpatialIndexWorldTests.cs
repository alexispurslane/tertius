using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The spatial index inside a running world, where step 8c of the sync point updates it. A world with several workers
/// finds the cells of the moved entities on the workers once enough entities moved. Here the threshold is forced to
/// one entity, so that every update of a small world takes that path.
/// </summary>
public sealed class SpatialIndexWorldTests
{
    // The workload moves, spawns and destroys entities in many frames, and hands bodies off between frames. After every
    // tick and after the changes between ticks, the audit compares radius queries with a search through every live
    // entity, and the world checks the index's invariants at every sync point.
    [Theory]
    [InlineData(20261018)]
    [InlineData(7)]
    public void AWorldThatFindsTheCellsOfItsMovedEntitiesOnTheWorkersAnswersEveryRadiusQueryRight(int seed)
    {
        using var workload = new ChaoticWorkload(seed: seed, maxDegreeOfParallelism: 4);
        workload.World.Spatial.ParallelThreshold = 1;

        workload.Run(100);

        workload.Tally.Moves.Should().BeGreaterThan(50);
        workload.History.SpatialMatches.Should().BeGreaterThan(500);
    }
}
