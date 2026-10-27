using Tertius.Simulation;

namespace Tertius.Physics.Tests;

/// <summary>
/// The pieces of the puffs that run on the gameplay workers (docs/plans/particles.md, "The parallel pieces"): the step of
/// 2b in equal slices of the rows. A world on four workers, with each threshold forced down to 1 so that every step of its
/// small table takes the workers, must keep the rows, the ranges, the looks and the snapshot's puffs of a world on one
/// thread, bit for bit, in every tick.
/// </summary>
public sealed class PuffsOnTheWorkersTests
{
    [Fact]
    public void AWorldOnFourWorkersKeepsThePuffsOfAWorldOnOneThreadBitForBit()
    {
        using World one = PuffTesting.BusySceneWithCamera();
        using World four = PuffTesting.BusySceneWithCamera(maxDegreeOfParallelism: 4);
        four.Puffs.StepParallelThreshold = 1;

        for (int tick = 0; tick < 300; tick++)
        {
            one.Tick();
            four.Tick();
            // Every tick, so that a difference shows in the tick that makes it: the first ticks hold fewer puffs than slices.
            PuffTesting.TableHash(four).Should().Be(PuffTesting.TableHash(one), $"the tables agree after tick {tick}");
            WorldHash
                .OfPuffs(PuffTesting.Latest(four))
                .Should()
                .Be(WorldHash.OfPuffs(PuffTesting.Latest(one)), $"the snapshots agree after tick {tick}");
        }

        four.Puffs.Count.Should().Be(500, "the cap binds, so the steps also drop puffs at the end of their lives");
        four.Puffs.View.Ranges.Length.Should().BeGreaterThan(1, "the slices cross the bounds of the ranges");
        four.Errors.Should().BeEmpty();
    }
}
