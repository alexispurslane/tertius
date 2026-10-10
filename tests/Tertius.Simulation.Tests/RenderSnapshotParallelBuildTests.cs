using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A large world builds its snapshot on several threads, one slice of the entity table for each worker, and joins the
/// slices in slot order. The result must not depend on how many threads there were. This is a determinism test, so it is
/// one of the few that use several threads.
/// </summary>
public class RenderSnapshotParallelBuildTests
{
    /// <summary>The number of entities, enough to pass the size at which the builder goes parallel (8192 slots).</summary>
    private const int EntityCount = 12_000;

    private static RenderSnapshot BuildAfterTwoTicks(int maxDegreeOfParallelism)
    {
        // Debug checks cost O(capacity) per tick, which adds up for 12 000 entities and says nothing about the snapshot.
        using var test = new TestWorld(maxDegreeOfParallelism: maxDegreeOfParallelism, debugChecks: false);
        FrameHandle side = test.SpawnStaticFrame(new DPose(new Double3(1e9, 5, 5), PoseChecks.QuarterTurnAboutY), radius: 1e6f);
        for (int i = 0; i < EntityCount; i++)
        {
            test.World.Spawn(new SpawnDesc
            {
                Frame = i % 3 == 0 ? side : test.Root,
                Pose = TestWorld.At(i * 0.5, i % 7, -i % 11),
                Render = Visible(i % 5),
                Components = i % 4 == 0 ? [new Mover(new Double3(0.25, 0, 0))] : [],
            });
        }
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(100));

        test.Run(2);

        // Disposing the world stops its physics thread and nothing else, so the snapshot object stays valid after it.
        return test.World.Latest();
    }

    [Fact]
    public void TheSnapshotOfAHugeWorldIsTheSameWhateverTheNumberOfThreads()
    {
        RenderSnapshot sequential = BuildAfterTwoTicks(maxDegreeOfParallelism: 1);
        RenderSnapshot parallel = BuildAfterTwoTicks(maxDegreeOfParallelism: 8);

        sequential.Instances.Should().HaveCount(EntityCount);
        parallel.Instances.Should().Equal(sequential.Instances);
        parallel.OriginInFrame.Should().Be(sequential.OriginInFrame);
    }

    [Fact]
    public void TheInstancesOfAHugeWorldAreInSlotOrderWhateverTheNumberOfThreads()
    {
        RenderSnapshot parallel = BuildAfterTwoTicks(maxDegreeOfParallelism: 8);

        parallel.Instances.Select(i => i.EntityIndex).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }
}
