using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// When the step of 2b takes the workers (docs/plans/particles.md, "The parallel pieces"): from
/// <see cref="PuffTable.StepParallelThreshold"/> live puffs on, in a table with workers. The table is built by hand, as in
/// PuffRuleTests, on four workers that count their tasks, so a test sees whether a step took them. PuffsOnTheWorkersTests,
/// in the physics tests, compares the steps on the workers with the steps on one thread.
/// </summary>
public sealed class PuffStepOnWorkersTests
{
    /// <summary>The World's step: the float 1/60 widened to a double, 0.01666666753590107 s.</summary>
    private const float Dt = 1f / 60;

    private const int Steps = 30;

    private static readonly PuffLook Look = new(
        Life: 10,
        StartRadius: 0.2f,
        EndRadius: 1,
        TemperatureRatio: 3,
        Shading: PuffShading.Glow(new(4, 3, 2))
    );

    private readonly CountingScheduler workers = new();
    private readonly PuffTable puffs;
    private long tick;

    /// <summary>
    /// A table on four workers that count their tasks, after <see cref="Steps"/> steps of an emitter at 60 puffs per second,
    /// one per step, at the default threshold.
    /// </summary>
    public PuffStepOnWorkersTests()
    {
        EngineEvents.Register();
        EntityTable table = TableSupport.NewTable();
        var frames = new FrameTree();
        FrameHandle space = frames.Create(
            frameOwner: table.SpawnLive(),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e6f)
        );
        table.Activate(
            entity: table.Reserve(),
            row: new()
            {
                Frame = space,
                PhysicsHome = space,
                Pose = DPose.Identity,
            },
            render: new() { Emitter = new(Rate: 60, Speed: 1, Spread: 0, Look: Look) },
            components: [],
            wants: [],
            body: null,
            bodyCenterOfMass: Vector3.Zero,
            ownedFrame: FrameHandle.Null
        );
        table.CopyDirtyRows();
        puffs = new PuffTable(
            entities: table,
            frames: frames,
            capacity: 4096,
            reportError: _ => { },
            workers: new() { MaxDegreeOfParallelism = 4, TaskScheduler = workers }
        );
        for (int i = 0; i < Steps; i++)
            StepOnce();
    }

    /// <summary>One step of 2b and the births of 7c, as in a sync point.</summary>
    private void StepOnce()
    {
        puffs.Step(tick, Dt);
        puffs.AddNewborns(tick, Dt);
        tick++;
    }

    [Fact]
    public void AStepOfAsManyPuffsAsTheThresholdRunsOnTheWorkers()
    {
        int before = puffs.Count;
        puffs.StepParallelThreshold = before;

        StepOnce();

        workers.Queued.Should().BeGreaterThan(0);
        puffs.Count.Should().Be(before + 1, "no puff dies, and the emitter makes one per step");
        puffs.AssertInvariants();
    }

    [Fact]
    public void AStepOfFewerPuffsThanTheThresholdStaysOnTheSyncThread()
    {
        int before = puffs.Count;
        puffs.StepParallelThreshold = before + 1;

        StepOnce();

        workers.Queued.Should().Be(0);
        puffs.Count.Should().Be(before + 1, "no puff dies, and the emitter makes one per step");
        puffs.AssertInvariants();
    }

    [Fact]
    public void ATableWithWorkersStepsAFewPuffsOnTheSyncThread()
    {
        // The default threshold: a step must hold many puffs before the workers pay for themselves.
        StepOnce();

        workers.Queued.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AThresholdOfTheStepOnTheWorkersBelowOnePuffIsRefused(int threshold)
    {
        Action set = () => puffs.StepParallelThreshold = threshold;

        set.Should().Throw<ArgumentOutOfRangeException>();
        puffs
            .StepParallelThreshold.Should()
            .Be(PuffTable.DefaultStepParallelThreshold, "a refused threshold changes nothing");
    }
}
