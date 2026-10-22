using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The turns of the frames that the World fixes before each step (<see cref="FrameTree.BeginStep"/>,
/// docs/plans/spin-gravity.md, "V1, the turn of the step in flight", "V2, the turn of the step that ended" and "Frames without
/// a last turn"), on the World and the real backend: how a spawn starts a station's turns, and what a failed step does to
/// them. The station of the tests is a frame without a body that spins in a root frame, at 0.31321 rad/s, the rate that
/// gives 9.81 m/s² at a radius of 100 m.
/// </summary>
public class WorldFrameTurnTests
{
    private const double StationRate = 0.31321;
    private static readonly FrameSpin StationSpin = new(DQuaternion.Identity, StationRate);
    private static readonly FrameSpec Station = new(FrameBounds.Sphere(150));

    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    [Fact]
    public void AStationSpawnedWithItsFirstSpinTurnsSteadilyFromItsFirstStep()
    {
        using World world = NewWorld(out _);
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = Space(world), AsFrame = Station }
            .Spinning(world, StationSpin, firstMove: true)));

        world.Tick();

        FrameTurns first = world.Frames.Turns(station);
        first.Step.IsIdentity.Should().BeFalse("the station turns in its first step, by its first motion intent");
        first.Last.Should().Be(first.Step,
            "a new frame has no last turn, so its first step assumes a steady turn: no spin-up");
        first.Step.AngularVelocity.Y.Should().BeApproximately(StationRate, 1e-12);

        world.Tick();

        world.Frames.Turns(station).Last.Should()
            .Be(first.Step, "the station left the list of frames without a last turn");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AStationSpawnedWithoutAFirstMotionIntentHoldsStillInItsFirstStepAndSpinsUpInItsSecond()
    {
        using World world = NewWorld(out _);
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = Space(world), AsFrame = Station }
            .Spinning(world, StationSpin)));

        world.Tick();

        world.Frames.Turns(station).Should().Be(new FrameTurns(FrameTurn.None, FrameTurn.None),
            "the station's components first write a motion intent in the tick beside its first step");

        world.Tick();

        FrameTurns second = world.Frames.Turns(station);
        second.Last.Should().Be(FrameTurn.None);
        second.Step.AngularVelocity.Y.Should().BeApproximately(StationRate, 1e-12);
        second.AngularAcceleration(world.DeltaTime).Y.Should().BeApproximately(StationRate / world.DeltaTime, 1e-9,
            "the station turns at once from rest to its full rate in its second step");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AfterAStepThatFailedTheNextStepTakesTheNewTurnOfEveryFrameAsItsLastTurn()
    {
        const double spinUp = 0.01;
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle station =
            world.GetOwnedFrame(world.Spawn(SpinUp.Spawning(new() { Frame = Space(world), AsFrame = Station }, world,
                spinUp)));
        Tick(world, 3);
        world.Frames.Turns(station).AngularAcceleration(world.DeltaTime).Y.Should().BeApproximately(spinUp, 1e-9);

        faults.ThrowFromStep = new InvalidOperationException("The test fails this step on purpose.");
        world.Tick();
        world.Tick();

        FrameTurns afterFailure = world.Frames.Turns(station);
        afterFailure.Last.Should().Be(afterFailure.Step,
            "the results of the failed step were dropped, so the step after it assumes a steady turn");

        world.Tick();

        world.Frames.Turns(station).AngularAcceleration(world.DeltaTime).Y.Should().BeApproximately(spinUp, 1e-9,
            "the station left the list at the step after the failure");
        world.Errors.Should().ContainSingle().Which.Should().Contain("the physics step failed");
    }
}
