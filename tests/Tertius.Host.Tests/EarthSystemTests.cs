using System.Numerics;
using Tertius.Demo;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using EarthSystem = Tertius.Demo.DemoScenes.EarthSystem;

namespace Tertius.Host.Tests;

/// <summary>
/// The scene <c>earth-system</c>, built without a script, on the World and the real Jolt backend: the Sun at its true size,
/// Earth on its Kepler orbit round the Sun at the June solstice, the frame <c>earth</c> that does not turn, the frame
/// <c>earth-surface</c> that turns once per sidereal day and ends 100 km up, the Moon on its Kepler orbit in <c>earth</c>,
/// the demo's station on its orbit in <c>earth</c>, and a pad at the north pole in <c>earth-surface</c>.
/// </summary>
public sealed class EarthSystemTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();
    private readonly SceneInstance scene;
    private readonly FrameHandle sun, earth, surface;

    public EarthSystemTests()
    {
        scene = DemoScenes.All.Single(s => s.Name == "earth-system").Build(world, RenderResources.None);
        sun = world.GetOwnedFrame(scene["sun"]);
        earth = world.GetOwnedFrame(scene["earth"]);
        surface = world.GetOwnedFrame(scene["earth-surface"]);
    }

    public void Dispose() => world.Dispose();

    /// <summary>The dot product of two quaternions: ±1 for the same rotation.</summary>
    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    /// <summary>
    /// At the start, Earth stands at the June solstice: along +Z of the Sun's axes, so that the Sun, seen from Earth, stands
    /// toward -Z, the way the north pole leans. Over the next 10 s, sampled each second, earth's center follows Earth's
    /// Kepler orbit round the Sun, and its axes keep the obliquity's lean, with no turn.
    /// </summary>
    [Fact]
    public void EarthsFrameFollowsItsKeplerOrbitRoundTheSunAndDoesNotTurn()
    {
        KeplerOrbit orbit = DemoScenes.EarthOrbit(DemoScenes.EarthDate.JuneSolstice);
        DQuaternion lean = world.PoseIn(scene["earth"], sun).Rotation;
        world.PoseIn(scene["earth"], sun).Position.Normalized().Z.Should().BeApproximately(1, 1e-9, "Earth starts at the June solstice");

        for (int second = 1; second <= 10; second++)
        {
            Tick(world, 60);
            DPose pose = world.PoseIn(scene["earth"], sun);
            (pose.Position - orbit.StateAt(world.Time, DemoScenes.SunMu).Position).Length.Should().BeLessThan(1, $"earth is on its orbit after {second} s");
            Dot(pose.Rotation, lean).Should().BeApproximately(1, 1e-12, $"earth has not turned after {second} s");
            ((Double3)world.AngularVelocityIn(scene["earth"], sun)).Length.Should().BeLessThan(1e-12, $"earth does not turn after {second} s");
        }
    }

    /// <summary>
    /// earth-surface sits at earth's center, turns about earth's +Y, the north pole, at the sidereal rate, and its bubble
    /// is a sphere that ends at the top of the air, 100 km above Earth's mean radius.
    /// </summary>
    [Fact]
    public void EarthSurfaceTurnsAtTheSiderealRateInEarthAndItsBubbleEnds100KmUp()
    {
        Tick(world, 600);

        DPose pose = world.PoseIn(scene["earth-surface"], earth);
        pose.Position.Length.Should().BeLessThan(1e-6, "earth-surface sits at earth's center");
        DQuaternion turned = DQuaternion.FromAxisAngle(Double3.UnitY, DemoScenes.SiderealRate * world.Time);
        Math.Abs(Dot(pose.Rotation, turned)).Should().BeApproximately(1, 1e-12, $"earth-surface has turned by the sidereal rate times {world.Time} s");
        ((Double3)world.AngularVelocityIn(scene["earth-surface"], earth) - Double3.UnitY * DemoScenes.SiderealRate).Length
            .Should().BeLessThan(1e-9, "earth-surface turns about the pole at the sidereal rate");
        world.Frames.Bounds(surface).Should().Be(FrameBounds.Sphere((float)(DemoScenes.EarthRadius + 100e3)), "the bubble ends 100 km up");
    }

    /// <summary>
    /// The Moon lies in earth, and over 10 s, sampled each second, its center follows the Moon's Kepler orbit round Earth,
    /// whose plane leans 5.145 degrees from the ecliptic, and the Moon does not turn.
    /// </summary>
    [Fact]
    public void TheMoonFollowsItsOrbitInEarthAndDoesNotTurn()
    {
        Entity moon = scene["moon"];
        world.GetFrame(moon).Should().Be(earth);
        DQuaternion start = world.PoseIn(moon, earth).Rotation;

        for (int second = 1; second <= 10; second++)
        {
            Tick(world, 60);
            DPose pose = world.PoseIn(moon, earth);
            (pose.Position - DemoScenes.MoonOrbit.StateAt(world.Time, DemoScenes.EarthMu).Position).Length.Should().BeLessThan(1e-3, $"the Moon is on its orbit after {second} s");
            Dot(pose.Rotation, start).Should().BeApproximately(1, 1e-12, $"the Moon has not turned after {second} s");
        }

        // The ecliptic's pole in earth's axes: the Sun's +Y, seen in earth's leaning axes.
        Double3 eclipticPole = world.PoseIn(scene["earth"], sun).Rotation.Conjugate().Rotate(Double3.UnitY);
        Double3 normal = Double3.Cross(world.PoseIn(moon, earth).Position, world.VelocityIn(moon, earth)).Normalized();
        (Math.Acos(Double3.Dot(normal, eclipticPole)) * 180 / Math.PI).Should().BeApproximately(5.145, 1e-4, "the Moon's orbit leans 5.145 degrees from the ecliptic");
    }

    /// <summary>
    /// Over 10 s, sampled each second, the demo's station stays in earth and its center follows the station's orbit, the
    /// polar circle 400 km up, there.
    /// </summary>
    [Fact]
    public void TheStationStaysInEarthOnItsOrbit()
    {
        Entity station = scene["station"];
        for (int second = 1; second <= 10; second++)
        {
            Tick(world, 60);
            world.GetFrame(station).Should().Be(earth, $"the station is in earth after {second} s");
            (world.PoseIn(station, earth).Position - DemoStation.Orbit.StateAt(world.Time, DemoScenes.EarthMu).Position).Length
                .Should().BeLessThan(0.01, $"the station is on its orbit after {second} s");
        }
    }

    /// <summary>
    /// A box of 1 m dropped 0.05 m onto the pad at the north pole, in earth-surface, settles there and stays at rest in
    /// earth-surface for 5 s: on the axis of the turn, it feels Earth's pull and no centrifugal or Coriolis force.
    /// </summary>
    [Fact]
    public void ABoxRestingOnThePadAtThePoleStaysAtRestInEarthSurface()
    {
        var restingOn = new Double3(0, EarthSystem.PadTop + 0.5, 0);
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = surface,
            Pose = new DPose(restingOn + new Double3(0, 0.05, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 100),
        });

        Tick(world, 5 * 60);

        world.GetFrame(box).Should().Be(surface);
        (world.PoseIn(box, surface).Position - restingOn).Length.Should().BeLessThan(0.02, "the box rests on the pad");
        world.VelocityIn(box, surface).Length.Should().BeLessThan(0.01, "the box is at rest in earth-surface");
    }

    /// <summary>The orbit scene keeps Earth at the March equinox: along -X of the Sun's axes, so the Sun stands toward +X from Earth.</summary>
    [Fact]
    public void TheOrbitSceneKeepsEarthAtTheMarchEquinox()
    {
        using World orbitWorld = HostTesting.NewJoltWorld();
        SceneInstance orbit = DemoScenes.All.Single(s => s.Name == "orbit").Build(orbitWorld, RenderResources.None);

        orbitWorld.PoseIn(orbit["earth"], orbitWorld.GetOwnedFrame(orbit["sun"])).Position.Normalized().X.Should().BeApproximately(-1, 1e-9);
    }
}
