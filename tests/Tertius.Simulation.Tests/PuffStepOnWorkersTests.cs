using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// When the step of 2b takes the workers (docs/plans/particles.md, "The parallel pieces"): from
/// <see cref="PuffTable.StepParallelThreshold"/> live puffs on, in a world with more than one thread. The table's workers
/// count their tasks here, so a test sees whether a step took them. PuffsOnTheWorkersTests, in the physics tests, compares
/// the steps on the workers with the steps on one thread.
/// </summary>
public sealed class PuffStepOnWorkersTests
{
    private const int Ticks = 30;

    private static readonly PuffLook Look = new(
        Life: 10,
        StartRadius: 0.2f,
        EndRadius: 1,
        TemperatureRatio: 3,
        Shading: PuffShading.Glow(new(4, 3, 2))
    );

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "workers")]
    private static extern ref ParallelOptions? WorkersOf(PuffTable table);

    /// <summary>
    /// A world without physics whose puff table steps on four workers that count their tasks, after <see cref="Ticks"/>
    /// ticks of an emitter at 60 puffs per second.
    /// </summary>
    private static World FilledWorld(out CountingScheduler workers)
    {
        EngineEvents.Register();
        var world = new World(new() { MaxDegreeOfParallelism = 4, DebugChecks = true });
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        world.Spawn(
            new()
            {
                Frame = space,
                Render = new RenderData { Emitter = new(Rate: 60, Speed: 1, Spread: 0, Look: Look) },
            }
        );
        for (int i = 0; i < Ticks; i++)
            world.Tick();
        workers = new CountingScheduler();
        WorkersOf(world.Puffs) = new() { MaxDegreeOfParallelism = 4, TaskScheduler = workers };
        return world;
    }

    [Fact]
    public void AStepOfAsManyPuffsAsTheThresholdRunsOnTheWorkers()
    {
        using World world = FilledWorld(out CountingScheduler workers);
        int before = world.Puffs.Count;
        world.Puffs.StepParallelThreshold = before;

        world.Tick();

        workers.Queued.Should().BeGreaterThan(0);
        world.Puffs.Count.Should().Be(before + 1, "no puff dies, and the emitter makes one per step");
    }

    [Fact]
    public void AStepOfFewerPuffsThanTheThresholdStaysOnTheSyncThread()
    {
        using World world = FilledWorld(out CountingScheduler workers);
        int before = world.Puffs.Count;
        world.Puffs.StepParallelThreshold = before + 1;

        world.Tick();

        workers.Queued.Should().Be(0);
        world.Puffs.Count.Should().Be(before + 1, "no puff dies, and the emitter makes one per step");
    }

    [Fact]
    public void AWorldWithWorkersStepsAFewPuffsOnTheSyncThread()
    {
        // The default threshold: a step must hold many puffs before the workers pay for themselves.
        using World world = FilledWorld(out CountingScheduler workers);

        world.Tick();

        workers.Queued.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AThresholdOfTheStepOnTheWorkersBelowOnePuffIsRefused(int threshold)
    {
        using World world = FilledWorld(out _);

        Action set = () => world.Puffs.StepParallelThreshold = threshold;

        set.Should().Throw<ArgumentOutOfRangeException>();
        world
            .Puffs.StepParallelThreshold.Should()
            .Be(PuffTable.DefaultStepParallelThreshold, "a refused threshold changes nothing");
    }
}
