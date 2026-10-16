using AwesomeAssertions;
using Tertius.Physics;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The world steps physics once per tick, on one thread of its own, while the gameplay chunks run. It waits for the step
/// at the sync point. A step that fails is reported and its output is not used; the world goes on. A broken contract is
/// a bug, not a failure, so it escapes the tick. These tests run the world on real Jolt. A failure comes from
/// <see cref="FaultInjectingBackend"/>, which throws from a step or a change batch on request.
/// </summary>
public class PhysicsStepTests
{
    /// <summary>
    /// Real Jolt, with a record of what each step received. It forwards every call and changes nothing. It exists because
    /// <see cref="FaultInjectingBackend"/> records the output of a step but not the context that the world handed in, and
    /// four tests below are about that context: the tick, the time and the thread.
    /// </summary>
    private sealed class StepRecorder(JoltPhysicsBackend jolt) : IPhysicsBackend
    {
        public List<StepRecord> Steps { get; } = [];

        public void ApplyChanges(PhysicsChanges changes, FrameTree frames) => jolt.ApplyChanges(changes, frames);

        public void Step(in PhysicsStepContext context, PhysicsStepOutput output)
        {
            Steps.Add(new StepRecord(context.Tick, context.Time, Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread));
            jolt.Step(context, output);
        }

        public float BoundingRadius(ShapeDesc shape) => jolt.BoundingRadius(shape);

        public string? ShapeProblem(ShapeDesc shape) => jolt.ShapeProblem(shape);

        public void Dispose() => jolt.Dispose();
    }

    /// <summary>A world with one gameplay thread on a recorder around a new Jolt backend. The world disposes the backend.</summary>
    private static World NewWorldRecording(out List<StepRecord> steps, Action<WorldOptions>? configure = null)
    {
        var recorder = new StepRecorder(new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 }));
        steps = recorder.Steps;
        var options = new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true };
        configure?.Invoke(options);
        return new World(options, recorder);
    }

    [Fact]
    public void PhysicsStepsOncePerTickWithTheNumberOfTheTick()
    {
        using World world = NewWorldRecording(out List<StepRecord> steps);
        Ground(world, 9.81f);

        world.Run(5);

        steps.Select(s => s.Tick).Should().Equal(0L, 1, 2, 3, 4);
    }

    [Fact]
    public void ThePhysicsTimeOfAStepIsTheSimulationTimeAtTheStartOfItsTick()
    {
        // 64 ticks per second is a power of two, so every time is exact: the step of tick 4 starts 4/64 s = 0.0625 s in.
        using World world = NewWorldRecording(out List<StepRecord> steps, options => options.TickRate = 64);
        Ground(world, 9.81f);

        world.Run(5);

        steps.Select(s => s.Time).Should().Equal(0, 1.0 / 64, 2.0 / 64, 3.0 / 64, 4.0 / 64);
    }

    [Fact]
    public void AllStepsRunOnTheSameThread()
    {
        List<StepRecord> steps;
        using (World world = NewWorldRecording(out steps))
        {
            Scenes.FloorWithBall(world);
            world.Run(5);
        }

        steps.Should().HaveCount(5);
        steps.Select(s => s.ThreadId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void TheStepThreadIsNeitherTheTickingThreadNorAThreadPoolThread()
    {
        List<StepRecord> steps;
        using (World world = NewWorldRecording(out steps))
        {
            Scenes.FloorWithBall(world);
            world.Run(5);
        }

        steps.Should().HaveCount(5).And.OnlyContain(s => s.ThreadId != Environment.CurrentManagedThreadId && !s.OnPoolThread);
    }

    [Fact]
    public void AWorldWithoutABackendRunsItsTicksWithoutPhysics()
    {
        using var test = new TestWorld();

        test.Run(3);

        test.World.TickNumber.Should().Be(3);
        test.World.Errors.Should().BeEmpty();
    }

    // The backend is real Jolt, so the test asks it whether it still works: a backend that the world disposed refuses
    // every call with an ObjectDisposedException.
    [Fact]
    public void TheWorldLeavesThePhysicsBackendUndisposedByDefault()
    {
        World world = NewWorld(out JoltPhysicsBackend backend, configure: options => options.DisposePhysics = false);
        using (backend)
        {
            world.Dispose();

            Action askTheBackend = () => backend.ShapeProblem(new SphereShapeDesc(1));

            askTheBackend.Should().NotThrow("the world did not dispose a backend that it was not asked to dispose");
        }
    }

    [Fact]
    public void TheWorldDisposesThePhysicsBackendWhenAsked()
    {
        World world = NewWorld(out JoltPhysicsBackend backend, configure: options => options.DisposePhysics = true);

        world.Dispose();

        Action askTheBackend = () => backend.ShapeProblem(new SphereShapeDesc(1));
        askTheBackend.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void AFailedStepIsReportedAndTheWorldGoesOn()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scenes.FloorWithBall(world);
        world.Run(1);
        faults.ThrowFromStep = new InvalidOperationException("solver exploded");

        world.Run(2);

        world.Errors.Should().ContainSingle().Which.Should().Contain("solver exploded");
        world.TickNumber.Should().Be(3);
    }

    [Fact]
    public void AFailedStepAppliesNoneOfTheOutputItWroteBeforeItFailed()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        DPose spawnPose = At(1, 10);
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = spawnPose, Body = Ball() });
        // Jolt writes the real state of the ball into the output. Then the step fails.
        BodyState written = default;
        faults.EditNextOutput = output =>
        {
            written = output.Bodies.Single(state => state.Entity == ball);
            throw new InvalidOperationException("solver exploded");
        };

        world.Run(1);

        written.Pose.Should().NotBe(spawnPose, "the step did write a new state for the ball before it failed");
        world.PoseInOwnFrame(ball).Should().Be(spawnPose, "the world uses none of the output of a step that threw");

        // The world also clears that output, so the next step does not carry the state of the step that failed.
        world.Run(1);

        faults.Outputs.Should().ContainSingle("the step that threw has no entry").Which.Bodies.Count(state => state.Entity == ball).Should().Be(1);
        world.PoseInOwnFrame(ball).Should().Be(faults.Outputs.Single().Bodies.Single(state => state.Entity == ball).Pose);
    }

    [Fact]
    public void AStepAfterAFailedStepWorksAsUsual()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        DPose spawnPose = At(1, 10);
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = spawnPose, Body = Ball() });
        faults.ThrowFromStep = new InvalidOperationException("solver exploded");

        world.Run(3);

        faults.Outputs.Should().HaveCount(2, "the steps of ticks 1 and 2 ran, and the step of tick 0 did not");
        BodyState last = faults.Outputs[^1].Bodies.Single(state => state.Entity == ball);
        last.Pose.Should().NotBe(spawnPose);
        world.PoseInOwnFrame(ball).Should().Be(last.Pose, "the state of the step of tick 2 is the last one applied");
        world.Errors.Should().ContainSingle();
    }

    [Fact]
    public void ABackendThatFailsToApplyChangesIsReportedAndTheWorldGoesOn()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        faults.ThrowFromApplyChanges = new InvalidOperationException("cannot create the body");
        FrameHandle ground = Ground(world, 9.81f);

        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 10), Body = Ball() });
        world.Run(2);

        world.Errors.Should().ContainSingle().Which.Should().Contain("cannot create the body");
        world.TickNumber.Should().Be(2);
    }

    [Fact]
    public void AContractThatTheBackendBreaksInAStepEscapesTheTickWithTheStackOfThePhysicsThread()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scenes.FloorWithBall(world);
        faults.ThrowFromStep = new ContractException("Invariant failed: the backend is broken");

        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage("*the backend is broken")
            .Which.StackTrace.Should().Contain($"{nameof(FaultInjectingBackend)}.{nameof(FaultInjectingBackend.Step)}");
        world.Errors.Should().BeEmpty("a broken contract is not a failed step");
    }

    [Fact]
    public void AContractThatTheBackendBreaksWhileItAppliesChangesEscapesTheTick()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        faults.ThrowFromApplyChanges = new ContractException("Invariant failed: the backend is broken");
        Scenes.FloorWithBall(world);

        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage("*the backend is broken")
            .Which.StackTrace.Should().Contain($"{nameof(FaultInjectingBackend)}.{nameof(FaultInjectingBackend.ApplyChanges)}");
        world.Errors.Should().BeEmpty("a broken contract is not a failure to apply changes");
    }
}
