using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Testing.WorldTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The pen's clip: it holds the demo's pen on the cabin's port wall while the cabin has weight, and once the cabin has
/// been weightless for <see cref="Clip.ReleaseSeconds"/>, it lets the pen go with a small push and a slow spin, so that
/// the pen floats into the middle of the cabin. On the World and the real Jolt backend. The shuttle is parked unturned in
/// a root frame without gravity, so its cabin is weightless, unless a test unparks it and runs its main engine at full
/// throttle, three gravities along its nose, -Z. The pen is the one of the demo scene (<see cref="DemoSurface.ClippedPen"/>):
/// 14 cm long, of 20 g, held 1.5 m above the floor, 1 cm from the port wall.
/// </summary>
public sealed class PenClipTests : IDisposable
{
    /// <summary>The pen of the demo. The world has no renderer, so the mesh and the material are only numbers.</summary>
    private static readonly CrateSpec Pen = new(DemoSurface.PenHalf, DemoSurface.PenMass, new MeshId(11), new MaterialId(5));

    private readonly World world = NewJoltWorld();
    private FrameHandle cabin;
    private Entity pen;
    private Entity person;

    public void Dispose() => world.Dispose();

    /// <summary>
    /// The shuttle parked in space, and the demo's pen in its clip. With <paramref name="thrust"/>, the shuttle unparks on
    /// its first tick and its main engine pushes at full throttle from then on. With <paramref name="passenger"/>, the
    /// demo's person stands at its seat as a <see cref="Passenger"/> that fetches the pen.
    /// </summary>
    private void Board(bool thrust = false, bool passenger = false)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        IComponent[] engine = thrust ? [new Unpark(), new MainEngine(Shuttle.Mass * Shuttle.ThrustAcceleration) { Throttle = 1 }] : [];
        Entity shuttle = Shuttle.Park(world, space, WorldTesting.At(0, Shuttle.Hull.Y), components: engine);
        cabin = world.GetOwnedFrame(shuttle);
        pen = world.Spawn(DemoSurface.ClippedPen(Pen, cabin));
        if (!passenger)
            return;
        person = world.Spawn(new SpawnDesc
        {
            Frame = cabin,
            Pose = new DPose(Shuttle.SeatStand, DQuaternion.Identity),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
            Render = new RenderData { ViewOffset = new Pose(DemoSurface.Eye, Quaternion.Identity) },
            Components = [new Passenger(new Waypoint(cabin, Shuttle.SeatStand), pen)],
        });
    }

    private Clip ClipOf() => world.GetComponent<Clip>(pen)!;

    /// <summary>How far the pen's center is from where the clip holds it, in meters.</summary>
    private double FromClip() => Double3.Distance(world.PoseIn(pen, cabin).Position, DemoSurface.PenClip.Position);

    /// <summary>
    /// The distance from <paramref name="position"/>, in the shuttle's axes, to the nearest face of the cabin, in meters.
    /// The cabin is a box centered on the shuttle's origin.
    /// </summary>
    internal static double Clearance(Double3 position) => Math.Min(Shuttle.Cabin.X - Math.Abs(position.X),
        Math.Min(Shuttle.Cabin.Y - Math.Abs(position.Y), Shuttle.Cabin.Z - Math.Abs(position.Z)));

    /// <summary>
    /// Three gravities along the nose for 4 s would throw a loose pen 11.5 m aft, onto the cabin's aft wall. The clip holds
    /// it at its place: each step, gravity moves the pen about 8 mm before the clip brings it back.
    /// </summary>
    [Fact]
    public void TheClipHoldsThePenAtItsPlaceWhileTheShuttleThrustsAtThreeGravities()
    {
        Board(thrust: true);
        double farthest = 0;

        for (int t = 0; t < 240; t++)
        {
            world.Tick();
            farthest = Math.Max(farthest, FromClip());
        }

        world.Frames.ProperAcceleration(cabin).Z.Should().BeApproximately(-Shuttle.ThrustAcceleration, 0.5f, "the shuttle thrusts along its nose");
        ClipOf().Holds.Should().BeTrue();
        farthest.Should().BeLessThan(0.015);
    }

    [Fact]
    public void InAWeightlessCabinTheClipHoldsThePenForASecondAndThenLetsItGo()
    {
        Board();
        double farthest = 0;

        for (int t = 0; t < (int)(Clip.ReleaseSeconds * 60) - 2; t++)
        {
            world.Tick();
            farthest = Math.Max(farthest, FromClip());
        }

        ClipOf().Holds.Should().BeTrue();
        farthest.Should().BeLessThan(0.001, "the clip holds the pen");
        Tick(world, 4);
        ClipOf().Holds.Should().BeFalse();
    }

    [Fact]
    public void TheClipLetsThePenGoWithASmallPushAndASlowSpin()
    {
        Board();
        TickUntil(world, () => !ClipOf().Holds, 120);

        Tick(world, 30);

        (world.VelocityIn(pen, cabin) - DemoSurface.PenPush).Length.Should().BeLessThan(1e-3);
        (world.AngularVelocityIn(pen, cabin) - DemoSurface.PenSpin).Length().Should().BeLessThan(0.01f);
    }

    /// <summary>
    /// For 10 s after the clip lets it go, nothing touches the pen, so it keeps the velocity of the push, and it drifts
    /// more than a meter from every face of the cabin.
    /// </summary>
    [Fact]
    public void ALetGoPenFloatsFreeIntoTheMiddleOfTheCabin()
    {
        Board();
        TickUntil(world, () => !ClipOf().Holds, 120);
        Tick(world, 2);
        double fastest = 0, slowest = double.PositiveInfinity;

        for (int t = 0; t < 600; t++)
        {
            world.Tick();
            double speed = world.VelocityIn(pen, cabin).Length;
            (fastest, slowest) = (Math.Max(fastest, speed), Math.Min(slowest, speed));
        }

        fastest.Should().BeApproximately(DemoSurface.PenPush.Length, 1e-3);
        slowest.Should().BeApproximately(DemoSurface.PenPush.Length, 1e-3);
        Clearance(world.PoseIn(pen, cabin).Position).Should().BeGreaterThan(1);
    }

    /// <summary>
    /// The passenger leaves its seat after the clip has let the pen go, floats to the pen, and takes it while the pen floats
    /// in the middle of the cabin.
    /// </summary>
    [Fact]
    public void ThePassengerTakesThePenThatTheClipLetGo()
    {
        Board(passenger: true);
        long released = -1;
        Double3 last = default;

        for (int t = 0; t < 1500 && world.IsAlive(pen); t++)
        {
            if (released < 0 && !ClipOf().Holds)
                released = world.TickNumber;
            last = world.PoseIn(pen, cabin).Position;
            world.Tick();
        }

        world.IsAlive(pen).Should().BeFalse("the passenger took the pen");
        released.Should().BeGreaterThan(0, "the clip let the pen go before the passenger took it");
        world.GetComponent<Passenger>(person)!.Phase.Should().Be(PassengerPhase.Holding);
        Clearance(last).Should().BeGreaterThan(0.5, "the pen floated in the middle of the cabin");
    }
}
