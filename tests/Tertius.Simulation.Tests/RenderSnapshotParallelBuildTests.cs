using System.Numerics;
using AwesomeAssertions;
using static Tertius.Simulation.Tests.RenderSnapshotSupport;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A large world builds its snapshot on the workers, one chunk of 1,024 slots of the entity table for each work item, and
/// joins the chunks in slot order. The result must not depend on how many threads there were. These are determinism
/// tests, so they are among the few that use several threads. The builder goes to the workers from
/// <see cref="RenderSnapshotBuilder.ParallelThreshold"/> slots on, so one test forces that threshold down to one slot,
/// and a small world then builds every snapshot on the workers.
/// </summary>
public class RenderSnapshotParallelBuildTests
{
    /// <summary>The number of entities, enough to pass the default size at which the builder goes parallel (8,192 slots).</summary>
    private const int EntityCount = 12_000;

    /// <summary>The number of entities of the small world: they fill three chunks of 1,024 slots, so a build on the workers has three work items.</summary>
    private const int SmallEntityCount = 2_600;

    /// <summary>
    /// A world of <paramref name="entityCount"/> visible entities in the root and in a far frame, every fourth one moving and
    /// every fiftieth one carrying a point light, after two ticks. With <paramref name="parallelThreshold"/>, the builder
    /// goes to the workers from that many slots on.
    /// </summary>
    private static RenderSnapshot BuildAfterTwoTicks(int maxDegreeOfParallelism, int entityCount, int? parallelThreshold = null)
    {
        // Debug checks cost O(capacity) per tick, which adds up for 12 000 entities and says nothing about the snapshot.
        using var test = new TestWorld(maxDegreeOfParallelism: maxDegreeOfParallelism, debugChecks: false);
        if (parallelThreshold is { } threshold)
            Inside.RenderBuilderOf(test.World).ParallelThreshold = threshold;
        FrameHandle side = test.SpawnStaticFrame(new DPose(new Double3(1e9, 5, 5), PoseChecks.QuarterTurnAboutY), radius: 1e6f);
        for (int i = 0; i < entityCount; i++)
        {
            RenderData render = Visible(i % 5);
            if (i % 50 == 0)
                render.Light = new PointLightData(Pose.Identity, new Vector3(1, 0.5f, 0.25f), Intensity: 1, Range: 20);
            test.World.Spawn(new SpawnDesc
            {
                Frame = i % 3 == 0 ? side : test.Root,
                Pose = TestWorld.At(i * 0.5, i % 7, -i % 11),
                Render = render,
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
        RenderSnapshot sequential = BuildAfterTwoTicks(maxDegreeOfParallelism: 1, EntityCount);
        RenderSnapshot parallel = BuildAfterTwoTicks(maxDegreeOfParallelism: 8, EntityCount);

        sequential.Instances.Should().HaveCount(EntityCount);
        parallel.Instances.Should().Equal(sequential.Instances);
        parallel.Lights.Should().Equal(sequential.Lights);
        parallel.OriginInFrame.Should().Be(sequential.OriginInFrame);
    }

    [Fact]
    public void TheInstancesOfAHugeWorldAreInSlotOrderWhateverTheNumberOfThreads()
    {
        RenderSnapshot parallel = BuildAfterTwoTicks(maxDegreeOfParallelism: 8, EntityCount);

        parallel.Instances.Select(i => i.EntityIndex).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void ASmallWorldThatBuildsOnTheWorkersFromOneSlotOnBuildsTheSnapshotOfAWorldOnOneThread()
    {
        RenderSnapshot serial = BuildAfterTwoTicks(maxDegreeOfParallelism: 1, SmallEntityCount);
        RenderSnapshot onWorkers = BuildAfterTwoTicks(maxDegreeOfParallelism: 4, SmallEntityCount, parallelThreshold: 1);

        serial.Instances.Should().HaveCount(SmallEntityCount);
        serial.Lights.Should().HaveCount(SmallEntityCount / 50);
        onWorkers.Instances.Should().Equal(serial.Instances);
        onWorkers.Lights.Should().Equal(serial.Lights);
        onWorkers.Camera.Should().Be(serial.Camera);
        onWorkers.OriginInFrame.Should().Be(serial.OriginInFrame);
    }

    // ---- From how many slots on --------------------------------------------------------------------------------------

    /// <summary>
    /// A builder over a table that holds the owner of a root frame, a camera in that frame and <paramref name="visible"/>
    /// visible entities in it, with four workers whose scheduler counts the tasks queued on it.
    /// </summary>
    private sealed class BuilderHarness
    {
        private readonly EntityTable entities;
        private readonly Entity camera;

        public BuilderHarness(int visible)
        {
            EngineEvents.Register();
            entities = new EntityTable(chunkSize: 64, initialCapacity: visible + 2);
            var frames = new FrameTree();
            Entity owner = entities.Reserve();
            FrameHandle root = frames.Create(owner, FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));
            entities.Activate(owner, new TransformRow { Pose = DPose.Identity }, new RenderData(), [], [], null, Vector3.Zero, root);
            camera = Spawn(root, new RenderData(), 0);
            for (int i = 0; i < visible; i++)
                Spawn(root, Visible(), i + 1);
            frames.Update();
            Builder = new RenderSnapshotBuilder(entities, frames, new ParallelOptions { MaxDegreeOfParallelism = 4, TaskScheduler = Workers });
        }

        public CountingScheduler Workers { get; } = new();
        public RenderSnapshotBuilder Builder { get; }

        /// <summary>How many slots the table has handed out: the owner of the root, the camera and the visible entities.</summary>
        public int SlotCount => entities.SlotCount;

        public RenderSnapshot Build()
        {
            var snapshot = new RenderSnapshot();
            Builder.Build(snapshot, new RenderView(Tick: 0, Time: 0, camera, VerticalFov: 1, FrameHandle.Null, Vector3.One, SunIntensity: 0, new AmbientLight(), ShowFrameOutlines: false));
            return snapshot;
        }

        private Entity Spawn(FrameHandle frame, in RenderData render, double x)
        {
            Entity entity = entities.Reserve();
            entities.Activate(entity, new TransformRow { Frame = frame, PhysicsHome = frame, Pose = TestWorld.At(x) }, render, [], [], null, Vector3.Zero, FrameHandle.Null);
            return entity;
        }
    }

    [Fact]
    public void ABuildOfFewerSlotsThanTheThresholdStaysOnTheCallingThread()
    {
        var h = new BuilderHarness(visible: 2_000);
        h.Builder.ParallelThreshold = h.SlotCount + 1;

        RenderSnapshot snapshot = h.Build();

        h.Workers.Queued.Should().Be(0);
        snapshot.Instances.Should().HaveCount(2_000);
    }

    [Fact]
    public void ABuildOfAsManySlotsAsTheThresholdRunsOnTheWorkers()
    {
        var h = new BuilderHarness(visible: 2_000);
        h.Builder.ParallelThreshold = h.SlotCount;

        RenderSnapshot snapshot = h.Build();

        h.Workers.Queued.Should().BeGreaterThan(0);
        snapshot.Instances.Should().HaveCount(2_000);
    }

    [Fact]
    public void ABuilderWithWorkersBuildsTheSnapshotOfASmallWorldOnTheCallingThread()
    {
        // The default threshold: a build must be large before the workers pay for themselves.
        var h = new BuilderHarness(visible: 2_000);

        h.Build();

        h.Workers.Queued.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AThresholdOfTheBuildOnTheWorkersBelowOneSlotIsRefused(int threshold)
    {
        var h = new BuilderHarness(visible: 1);

        Action set = () => h.Builder.ParallelThreshold = threshold;

        set.Should().Throw<ArgumentOutOfRangeException>();
        h.Builder.ParallelThreshold.Should().Be(RenderSnapshotBuilder.DefaultParallelThreshold, "a refused threshold changes nothing");
    }
}
