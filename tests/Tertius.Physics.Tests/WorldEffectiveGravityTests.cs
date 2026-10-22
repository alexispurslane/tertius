using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The effective gravity of a turning frame on the World and the real backend (docs/plans/spin-gravity.md, "V5, the
/// effective gravity"): the linear gravity plus the centrifugal and the Euler term of the turns that the World fixes before
/// each step, and the readers in physics that take it as one continuous value. The station of the tests is a frame without
/// a body and without a field in a root frame, which turns about its +Y. A rate ω about +Y gives a body at (r, 0, 0) the
/// centrifugal acceleration (ω² r, 0, 0), outward, and an angular acceleration α about +Y the Euler acceleration
/// (0, 0, α r), against the turn.
/// </summary>
public class WorldEffectiveGravityTests
{
    private const double StationRate = 0.31321;
    private static readonly FrameSpin StationSpin = new(DQuaternion.Identity, StationRate);
    private static readonly FrameSpec Station = new(FrameBounds.Sphere(150));
    private static readonly Double3 Rim = new(100, 0, 0);
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    [Fact]
    public void AtTheRimOfAStationThatSpinsUpABodyFeelsTheCentrifugalAndTheEulerTermOfTheLastStep()
    {
        // The station spins up at 0.5 rad/s² from its spawn. After a tick, the turns describe the step that ended at the
        // world's time, when the station turns at α t. A body at the rim feels ω² r outward along +X and α r = 50 m/s²
        // against the turn along +Z.
        const double spinUp = 0.5;
        using World world = NewWorld(out _);
        double start = world.Time;
        FrameHandle station =
            world.GetOwnedFrame(world.Spawn(SpinUp.Spawning(new() { Frame = Space(world), AsFrame = Station }, world,
                spinUp)));
        Tick(world, 3);

        double rate = spinUp * (world.Time - start);
        Double3.Distance(world.Frames.EffectiveGravity(station, Rim), new(rate * rate * 100, 0, spinUp * 100)).Should()
            .BeLessThan(1e-9);
        world.Frames.LinearGravity(station, Rim).Should()
            .Be(Double3.Zero, "the station holds no field and does not accelerate");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APersonSpawnedAtTheRimOfASpinningStationStartsWithItsHeadTowardTheAxis()
    {
        // The station holds no field, so only the centrifugal term pulls the person, outward along +X: its up at its creation
        // is against that, toward the axis.
        using World world = NewWorld(out _);
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = Space(world), AsFrame = Station }
            .Spinning(world, StationSpin, firstMove: true)));
        Tick(world, 3);

        Entity person = world.Spawn(new() { Frame = station, Pose = new(Rim, DQuaternion.Identity), Body = Person });
        world.Tick();

        Vector3 up = world.PoseInOwnFrame(person).Rotation.Rotate(Vector3.UnitY);
        Vector3.Distance(up, -Vector3.UnitX).Should().BeLessThan(1e-5f, $"the person's up is {up}");
        world.Errors.Should().BeEmpty();
    }
}
