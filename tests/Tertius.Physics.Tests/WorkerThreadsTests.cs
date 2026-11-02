using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The number of Jolt worker threads of a backend (<see cref="JoltBackendOptions.WorkerThreads"/>). A default backend runs
/// one worker per processor that the runtime reports, minus one, so that <c>DOTNET_PROCESSOR_COUNT</c> bounds the native
/// workers of a test host as it bounds its xunit threads. An explicit count holds. The step ends in the same bits with any
/// count, so the count changes the speed of a step and never its result.
/// </summary>
public sealed class WorkerThreadsTests
{
    [Fact]
    public void ADefaultBackendRunsOneWorkerPerProcessorMinusOne() =>
        new JoltBackendOptions().WorkerThreadsFor(processorCount: 4).Should().Be(3);

    [Fact]
    public void ADefaultBackendOnOneProcessorRunsOneWorker() =>
        new JoltBackendOptions()
            .WorkerThreadsFor(processorCount: 1)
            .Should()
            .Be(1, "a job system without workers would run every job on the physics thread");

    [Fact]
    public void AnExplicitWorkerCountHoldsWhateverTheProcessorCount() =>
        new JoltBackendOptions { WorkerThreads = 6 }.WorkerThreadsFor(processorCount: 4).Should().Be(6);

    [Fact]
    public void ADefaultBackendTakesItsWorkerCountFromTheProcessorCountOfTheRuntime()
    {
        using var backend = new JoltPhysicsBackend();

        backend
            .WorkerThreads.Should()
            .Be(
                new JoltBackendOptions().WorkerThreadsFor(Environment.ProcessorCount),
                "DOTNET_PROCESSOR_COUNT sets Environment.ProcessorCount, and the hardware does not"
            );
    }

    [Fact]
    public void ABackendWithAnExplicitWorkerCountGivesJoltThatCount()
    {
        using var backend = new JoltPhysicsBackend(new() { WorkerThreads = 2 });

        backend.WorkerThreads.Should().Be(2);
    }

    /// <summary>
    /// The evidence that a change of the default count changes no result: Jolt's documentation makes its determinism
    /// depend on the order of the API calls and on the binary, not on the job system, and its own determinism tests compare
    /// 0 workers with 15. This heap is large enough that Jolt splits its island into batches for the workers.
    /// </summary>
    [Fact]
    public void AHeapEndsInTheSameBitsWithOneThreeOrNineWorkers()
    {
        ulong one = HeapRun(workers: 1);

        HeapRun(workers: 3).Should().Be(one, "3 is the default count of a host with DOTNET_PROCESSOR_COUNT=4");
        HeapRun(workers: 9).Should().Be(one, "9 is the default count on a machine of 10 cores");
    }

    /// <summary>
    /// The hash of a world after three seconds on a backend with <paramref name="workers"/> Jolt workers: 100 cubes of 1 m
    /// in 4 layers of 5 by 5, 2 cm apart, turned a little and dropped from uneven heights onto a floor and onto each other.
    /// The block that they land in is one island of about 260 contacts: 25 on the floor, 75 between the layers and 160
    /// between neighbors in a layer. That is more than 128, the size above which Jolt's large island splitter hands batches
    /// of an island's constraints to the workers.
    /// </summary>
    private static ulong HeapRun(int workers)
    {
        using var world = new World(
            new()
            {
                MaxDegreeOfParallelism = 1,
                InitialCapacity = 128,
                DebugChecks = false,
                DisposePhysics = true,
            },
            new JoltPhysicsBackend(new() { WorkerThreads = workers })
        );
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        for (int i = 0; i < 100; i++)
            world.Spawn(
                new()
                {
                    Frame = ground,
                    Pose = new(
                        new(i % 5 * 1.02, 0.6 + i / 25 * 1.1 + i % 7 * 0.01, i / 5 % 5 * 1.02),
                        DQuaternion.FromAxisAngle(Double3.UnitY, i % 5 * 0.005)
                    ),
                    Body = Cube(),
                }
            );
        for (int tick = 0; tick < 180; tick++)
            world.Tick();
        return WorldHash.Of(world);
    }
}
