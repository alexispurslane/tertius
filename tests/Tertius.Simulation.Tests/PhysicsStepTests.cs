using System.Numerics;
using AwesomeAssertions;
using NSubstitute;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The world steps physics once per tick, on one thread of its own, while the gameplay chunks run. It waits for the step
/// at the sync point. A step that fails is reported and its output is not used; the world goes on.
/// </summary>
public class PhysicsStepTests
{
    [Fact]
    public void PhysicsStepsOncePerTickWithTheNumberOfTheTick()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<StepRecord> steps = physics.RecordSteps();
        using var test = new TestWorld(physics);

        test.Run(5);

        steps.Select(s => s.Tick).Should().Equal(0L, 1, 2, 3, 4);
    }

    [Fact]
    public void ThePhysicsTimeOfAStepIsTheSimulationTimeAtTheStartOfItsTick()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<StepRecord> steps = physics.RecordSteps();
        // 64 ticks per second is a power of two, so every time is exact: the step of tick 4 starts 4/64 s = 0.0625 s in.
        using var test = new TestWorld(physics, configure: options => options.TickRate = 64);

        test.Run(5);

        steps.Select(s => s.Time).Should().Equal(0, 1.0 / 64, 2.0 / 64, 3.0 / 64, 4.0 / 64);
    }

    [Fact]
    public void AllStepsRunOnTheSameThread()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<StepRecord> steps = physics.RecordSteps();
        using (var test = new TestWorld(physics))
        {
            test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
            test.Run(5);
        }

        steps.Select(s => s.ThreadId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void TheStepThreadIsNeitherTheTickingThreadNorAThreadPoolThread()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        List<StepRecord> steps = physics.RecordSteps();
        using (var test = new TestWorld(physics))
        {
            test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
            test.Run(5);
        }

        steps.Should().OnlyContain(s => s.ThreadId != Environment.CurrentManagedThreadId && !s.OnPoolThread);
    }

    [Fact]
    public void AWorldWithoutABackendRunsItsTicksWithoutPhysics()
    {
        using var test = new TestWorld();

        test.Run(3);

        test.World.TickNumber.Should().Be(3);
        test.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheWorldLeavesThePhysicsBackendUndisposedByDefault()
    {
        IPhysicsBackend physics = TestPhysics.Create();

        new World(new WorldOptions(), physics).Dispose();

        physics.DidNotReceive().Dispose();
    }

    [Fact]
    public void TheWorldDisposesThePhysicsBackendWhenAsked()
    {
        IPhysicsBackend physics = TestPhysics.Create();

        new World(new WorldOptions { DisposePhysics = true }, physics).Dispose();

        physics.Received(1).Dispose();
    }

    [Fact]
    public void AFailedStepIsReportedAndTheWorldGoesOn()
    {
        IPhysicsBackend physics = TestPhysics.Create(onStep: (context, output) =>
        {
            if (context.Tick == 1)
                throw new InvalidOperationException("solver exploded");
        });
        using var test = new TestWorld(physics);

        test.Run(3);

        test.World.Errors.Should().ContainSingle().Which.Should().Contain("solver exploded");
        test.World.TickNumber.Should().Be(3);
    }

    [Fact]
    public void AFailedStepAppliesNoneOfTheOutputItWroteBeforeItFailed()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(1), Body = TestWorld.Ball });
        physics.OnStep((context, output) =>
        {
            output.Bodies.Add(new BodyState(ball, test.Root, TestWorld.At(99), Vector3.Zero, Vector3.Zero));
            throw new InvalidOperationException("solver exploded");
        });

        test.Run(1);

        test.World.GetPose(ball).Position.X.Should().Be(1, "the world uses none of the output of a step that threw");
    }

    [Fact]
    public void AStepAfterAFailedStepWorksAsUsual()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        using var test = new TestWorld(physics);
        Entity ball = test.World.Spawn(new SpawnDesc { Frame = test.Root, Pose = TestWorld.At(1), Body = TestWorld.Ball });
        physics.OnStep((context, output) =>
        {
            if (context.Tick == 0)
                throw new InvalidOperationException("solver exploded");
            output.Bodies.Add(new BodyState(ball, test.Root, TestWorld.At(context.Tick), Vector3.Zero, Vector3.Zero));
        });

        test.Run(3);

        test.World.GetPose(ball).Position.X.Should().Be(2, "the state of the step of tick 2 is the last one applied");
        test.World.Errors.Should().ContainSingle();
    }

    [Fact]
    public void ABackendThatFailsToApplyChangesIsReportedAndTheWorldGoesOn()
    {
        IPhysicsBackend physics = TestPhysics.Create();
        physics.When(p => p.ApplyChanges(Arg.Any<PhysicsChanges>(), Arg.Any<FrameTree>()))
            .Do(_ => throw new InvalidOperationException("cannot create the body"));
        using var test = new TestWorld(physics);

        test.World.Spawn(new SpawnDesc { Frame = test.Root, Body = TestWorld.Ball });
        test.Run(2);

        test.World.Errors.Should().NotBeEmpty().And.OnlyContain(e => e.Contains("cannot create the body"));
        test.World.TickNumber.Should().Be(2);
    }
}
