using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A long, seeded exposure of the whole simulation to the chaotic workload, with every invariant and the independent
/// audit checked after every tick. It answers "how long does the engine run before an invariant breaks?", which no
/// unit test does. The default exposure is short enough for every run of the suite. Set <c>TERTIUS_SOAK_SEEDS</c> and
/// <c>TERTIUS_SOAK_TICKS</c> for a real soak, and <c>TERTIUS_SOAK_WORKERS</c> to run the gameplay chunks on more than
/// one thread (the default is one, so that the default run is deterministic). The test prints the exposure and the mean
/// time between failures.
/// </summary>
public class SoakTests(ITestOutputHelper output)
{
    private const int FirstSeed = 5000;

    // Different chunk sizes put the chunk boundaries (and so the outbox merge order and the spawn pools) in different places.
    private static readonly int[] ChunkSizes = [4, 8, 16, 64];

    private static int FromEnvironment(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) && value > 0 ? value : fallback;

    [Fact]
    public void TheWorldSurvivesASeededSoakWithNoBrokenInvariant()
    {
        int seeds = FromEnvironment("TERTIUS_SOAK_SEEDS", 6);
        int ticksPerSeed = FromEnvironment("TERTIUS_SOAK_TICKS", 150);
        int workers = FromEnvironment("TERTIUS_SOAK_WORKERS", 1);
        long ticks = 0, entityTicks = 0;
        var failures = new List<string>();

        for (int i = 0; i < seeds; i++)
        {
            int seed = FirstSeed + i;
            using var workload = new ChaoticWorkload(seed: seed, chunkSize: ChunkSizes[i % ChunkSizes.Length],
                maxDegreeOfParallelism: workers);
            try
            {
                workload.Run(ticksPerSeed);
            }
            catch (Exception ex)
            {
                // Run puts the seed and the tick in the message. The soak goes on with the next seed, so that one report shows every failure.
                failures.Add(ex.Message);
            }

            ticks += workload.World.TickNumber;
            entityTicks += workload.Tally.EntityTicks;
        }

        // With no failure, the mean time between failures is at least a third of the exposure, with 95 percent confidence.
        string mtbf = failures.Count == 0 ? $"> {ticks / 3} ticks" : $"{ticks / failures.Count} ticks";
        output.WriteLine(
            $"soak: {seeds} seeds on {workers} workers, {ticks} ticks, {entityTicks} entity-ticks, {failures.Count} failures, MTBF {mtbf}");
        failures.Should().BeEmpty("exposure was {0} ticks over seeds {1} to {2}", ticks, FirstSeed,
            FirstSeed + seeds - 1);
    }
}
