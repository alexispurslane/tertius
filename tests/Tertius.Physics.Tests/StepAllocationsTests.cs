using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Runs the tests of <see cref="StepAllocationsTests"/> alone, because a <see cref="StepAllocations"/> swaps joltc's
/// contact callbacks for the whole process.
/// </summary>
[CollectionDefinition(nameof(StepAllocationsTests), DisableParallelization = true)]
public sealed class StepAllocationsCollection;

/// <summary>
/// <see cref="StepAllocations"/> counts what a window allocates on the threads that run the work of a world's steps, and
/// nothing that another thread of the process allocates in the same window.
/// </summary>
[Collection(nameof(StepAllocationsTests))]
public sealed class StepAllocationsTests
{
    private const int Kilobyte = 1024;
    private const int Megabyte = 1024 * Kilobyte;

    /// <summary>The window runs on the thread that ticks the world, so what the window allocates counts.</summary>
    [Fact]
    public void AnAllocationOnTheTickingThreadCounts()
    {
        using World world = NewWorldWithStepAllocations(out StepAllocations steps, out _, debugChecks: false);
        Falling(world);
        Tick(world, 10);
        byte[]? kept = null;

        long bytes = steps.Bytes(() =>
        {
            world.Tick();
            kept = new byte[Kilobyte];
        });

        kept.Should().NotBeNull();
        bytes.Should().BeGreaterThanOrEqualTo(Kilobyte);
    }

    /// <summary>
    /// A thread that the test starts runs no work of the step, so the megabyte that it allocates while the world ticks
    /// does not count, as the finalizer thread's trim of the shared array pool must not.
    /// </summary>
    [Fact]
    public void AnAllocationOnAThreadThatRunsNoWorkOfTheStepDoesNotCount()
    {
        using World world = NewWorldWithStepAllocations(out StepAllocations steps, out _, debugChecks: false);
        Falling(world);
        Tick(world, 10);
        byte[]? kept = null;
        var other = new Thread(() => kept = new byte[Megabyte]);

        long bytes = steps.Bytes(() =>
        {
            other.Start();
            world.Tick();
            other.Join();
        });

        kept.Should().NotBeNull();
        bytes.Should().BeLessThan(Megabyte, "the megabyte was allocated on a thread that runs no work of the step");
    }

    /// <summary>
    /// The edit of a step's output runs on the physics thread, inside the step, so the megabyte that it allocates counts,
    /// though the ticking thread did not allocate it.
    /// </summary>
    [Fact]
    public void AnAllocationOnThePhysicsThreadDuringAStepCounts()
    {
        var faults = new FaultInjectingBackend(new(JoltBackendOptions.Unpaced));
        using StepAllocations steps = new(faults);
        using World world = new(
            new WorldOptions
            {
                MaxDegreeOfParallelism = 1,
                InitialCapacity = TestCapacity,
                DebugChecks = false,
            },
            steps
        );
        Falling(world);
        Tick(world, 10);
        byte[]? kept = null;
        faults.EditNextOutput = _ => kept = new byte[Megabyte];

        long bytes = steps.Bytes(world.Tick);

        kept.Should().NotBeNull();
        bytes.Should().BeGreaterThanOrEqualTo(Megabyte, "the physics thread allocated the megabyte during the step");
    }

    /// <summary>
    /// A counter swaps joltc's contact callbacks for the whole process until it is disposed, so a second one while it
    /// lives would restore the wrong callbacks when it is disposed first.
    /// </summary>
    [Fact]
    public void ASecondCounterIsRefusedWhileTheFirstLives()
    {
        using World world = NewWorldWithStepAllocations(out _, out _, debugChecks: false);
        using JoltPhysicsBackend jolt = new(JoltBackendOptions.Unpaced);

        Action second = () => new StepAllocations(jolt).Dispose();

        second.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    /// <summary>A ball that falls in a frame with gravity, so that every step has an awake body.</summary>
    private static void Falling(World world)
    {
        FrameHandle ground = Ground(world, 9.81f);
        world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 1_000),
                Body = new(Shape: new SphereShapeDesc(0.5f), Mass: 1),
            }
        );
    }
}
