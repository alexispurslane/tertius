using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Testing.WorldTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;
using SpinUp = Tertius.Testing.SpinUp;

namespace Tertius.Host.Tests;

/// <summary>
/// The pen's clip: it holds the demo's pen on the cabin's port wall while the cabin has weight, and once the cabin has
/// been weightless for <see cref="Clip.ReleaseSeconds"/>, it lets the pen go with a small push and a slow spin, so that
/// the pen floats into the middle of the cabin. On the World and the real Jolt backend. The shuttle is parked unturned in
/// a root frame without gravity, so its cabin is weightless, unless a test unparks it and runs its main engine at full
/// throttle, three gravities along its nose, -Z. The pen is the one of the demo scene (
/// <see cref="DemoSurface.ClippedPen"/>):
/// 14 cm long, of 20 g, held 1.5 m above the floor, 1 cm from the port wall.
/// </summary>
public sealed class PenClipTests : IDisposable
{
    /// <summary>The pen of the demo. The world has no renderer, so the mesh and the material are only numbers.</summary>
    private static readonly CrateSpec Pen = new(
        HalfExtents: DemoSurface.PenHalf,
        Mass: DemoSurface.PenMass,
        Mesh: new(11),
        Material: new(5)
    );

    /// <summary>
    /// The person's turn in the demo's orbit: face-down, with its head toward the nose. Its up, +Y, points along the
    /// cabin's -Z, and its view, -Z, along the cabin's -Y, so the pen floats above its back when it leaves its seat.
    /// </summary>
    private static readonly DQuaternion FaceDown = DQuaternion.FromAxisAngle(Double3.UnitX, -Math.PI / 2);

    private World world = NewJoltWorld();
    private FrameHandle cabin;
    private Entity pen;
    private Entity person;

    public void Dispose() => world.Dispose();

    /// <summary>
    /// The shuttle parked in space, and the demo's pen in its clip. With <paramref name="thrust"/>, the shuttle unparks on
    /// its first tick and its main engine pushes at full throttle from then on. With <paramref name="passenger"/>, the
    /// demo's person floats at its seat as a <see cref="Passenger"/> that fetches the pen, turned as the climb leaves it in
    /// the demo (see <see cref="FaceDown"/>). The pen starts at its clip
    /// place, moving at <paramref name="penDrift"/>, in the shuttle's axes.
    /// </summary>
    private void Board(bool thrust = false, bool passenger = false, Double3 penDrift = default)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        IComponent[] engine = thrust
            ? [new Unpark(), new FullThrottle(), new MainEngine(Shuttle.Mass * Shuttle.ThrustAcceleration)]
            : [];
        Entity shuttle = Shuttle.Park(
            world: world,
            frame: space,
            pose: WorldTesting.At(0, Shuttle.Hull.Y),
            components: engine
        );
        cabin = world.GetOwnedFrame(shuttle);
        pen = world.Spawn(
            penDrift == default
                ? DemoSurface.ClippedPen(Pen, cabin)
                : Pen.Spawn(
                    cabin,
                    DemoSurface.PenClip,
                    penDrift,
                    new Clip(DemoSurface.PenClip, DemoSurface.PenPush, DemoSurface.PenSpin)
                )
        );
        if (!passenger)
            return;
        person = world.Spawn(
            new()
            {
                Frame = cabin,
                Pose = new(Shuttle.SeatStand, FaceDown),
                Body = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70),
                Render = new RenderData { ViewOffset = new(DemoSurface.Eye, Quaternion.Identity) },
                Components = [new Passenger(new(cabin, Shuttle.SeatStand), pen)],
            }
        );
    }

    private Clip ClipOf() => world.GetComponent<Clip>(pen)!;

    /// <summary>
    /// How far the pen's center is from the axis of the person's capsule, a segment 1.2 m long along the person's up
    /// through its center, in meters. The capsule's radius is 0.3 m, so the body cannot touch the pen, 7 cm from its center
    /// to its ends, while this is more than 0.37 m.
    /// </summary>
    private double PenFromBodyAxis()
    {
        DPose body = world.PoseIn(person, cabin);
        Double3 local = body.InverseTransformPoint(world.PoseIn(pen, cabin).Position);
        return (local - new Double3(0, Math.Clamp(local.Y, -0.6, 0.6), 0)).Length;
    }

    /// <summary>How far the pen's center is from where the clip holds it, in meters.</summary>
    private double FromClip() => Double3.Distance(world.PoseIn(pen, cabin).Position, DemoSurface.PenClip.Position);

    /// <summary>
    /// The distance from <paramref name="position"/>, in the shuttle's axes, to the nearest face of the cabin, in meters.
    /// The cabin is a box centered on the shuttle's origin.
    /// </summary>
    internal static double Clearance(Double3 position) =>
        Math.Min(
            Shuttle.Cabin.X - Math.Abs(position.X),
            Math.Min(Shuttle.Cabin.Y - Math.Abs(position.Y), Shuttle.Cabin.Z - Math.Abs(position.Z))
        );

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

        world
            .Frames.ProperAcceleration(cabin)
            .Z.Should()
            .BeApproximately(-Shuttle.ThrustAcceleration, 0.5f, "the shuttle thrusts along its nose");
        ClipOf().State.Value.Should().BeOfType<Clipped>();
        farthest.Should().BeLessThan(0.015);
    }

    /// <summary>
    /// The cabin is weightless from the start, and the pen starts with a drift of 0.1 m/s across the cabin, which would
    /// carry a loose pen 0.1 m in the second. The clip holds the pen within millimeters of its place for that second, and
    /// then lets it go.
    /// </summary>
    [Fact]
    public void InAWeightlessCabinTheClipHoldsThePenForASecondAndThenLetsItGo()
    {
        Board(penDrift: new(0.1, 0, 0));
        double farthest = 0;

        for (int t = 0; t < (int)(Clip.ReleaseSeconds * 60) - 2; t++)
        {
            world.Tick();
            farthest = Math.Max(farthest, FromClip());
        }

        ClipOf().State.Value.Should().BeOfType<Clipped>();
        farthest.Should().BeLessThan(0.005, "the clip holds the pen");
        Tick(world, 4);
        ClipOf().State.Value.Should().BeOfType<Released>();
    }

    /// <summary>
    /// At 49 ticks a second, a second of weightless ticks is 49 ticks, as the world clock divides them. A clip that summed
    /// tick lengths would wait a tick longer: 49 · (1.0 / 49) rounds to the double below 1.
    /// </summary>
    [Fact]
    public void AtFortyNineTicksASecondTheClipLetsThePenGoAtItsFortyNinthWeightlessTick()
    {
        world.Dispose();
        world = NewJoltWorld(tickRate: 49);
        Board();

        WorldTesting.TickUntil(world, () => ClipOf().State.Value is Clipped { WeightlessTicks: 48 }, limit: 120);
        ClipOf().State.Value.Should().BeOfType<Clipped>().Which.WeightlessTicks.Should().Be(48);
        world.Tick();

        ClipOf().State.Value.Should().BeOfType<Released>();
    }

    /// <summary>
    /// A frame without a body and without a field, in a root frame without gravity, spins up about its +Y at 0.05 rad/s²,
    /// with the pen in its clip at the place of the demo, 2.96 m from the axis. Its proper acceleration is zero, but the
    /// pen feels the Euler term of the change of the turn there, α r = 0.15 m/s², and a centrifugal term that grows. So the
    /// frame is never weightless at the clip, and the clip holds the pen. A clip that read the field less the proper
    /// acceleration would let it go after a second.
    /// </summary>
    [Fact]
    public void InAFrameWhoseTurnSpeedsUpTheClipHoldsThePen()
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        cabin = world.GetOwnedFrame(
            world.Spawn(SpinUp.Spawning(new() { Frame = space, AsFrame = new(FrameBounds.Sphere(10)) }, world, 0.05))
        );
        pen = world.Spawn(DemoSurface.ClippedPen(Pen, cabin));

        Tick(world, (int)(3 * Clip.ReleaseSeconds * 60));

        ClipOf().State.Value.Should().BeOfType<Clipped>();
        FromClip().Should().BeLessThan(0.005, "the clip holds the pen");
    }

    [Fact]
    public void TheClipLetsThePenGoWithASmallPushAndASlowSpin()
    {
        Board();
        TickUntil(world, () => ClipOf().State.Value is Released, 120);

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
        TickUntil(world, () => ClipOf().State.Value is Released, 120);
        Tick(world, 2);
        double fastest = 0,
            slowest = double.PositiveInfinity;

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
            if (released < 0 && ClipOf().State.Value is Released)
                released = world.TickNumber;
            last = world.PoseIn(pen, cabin).Position;
            world.Tick();
        }

        world.IsAlive(pen).Should().BeFalse("the passenger took the pen");
        released.Should().BeGreaterThan(0, "the clip let the pen go before the passenger took it");
        world
            .GetComponent<Passenger>(person)!
            .State.TryGetValue(out Holding holding)
            .Should()
            .BeTrue("the passenger holds the pen");
        world.IsAlive(holding.Copy).Should().BeTrue();
        Clearance(last).Should().BeGreaterThan(0.5, "the pen floated in the middle of the cabin");
    }

    /// <summary>
    /// The passenger's hand takes the drifting, tumbling pen where and as it floats: in the tick in which the drawn copy in
    /// the hand first shows, the loose pen still shows too, for its last tick, and the two lie within a centimeter and a
    /// degree of each other. So the pen does not jump into the hand.
    /// </summary>
    [Fact]
    public void ThePassengerGripsThePenWhereAndAsItFloats()
    {
        Board(passenger: true);

        TickUntil(world, () => world.IsAlive(pen) && HeldCopies().Count > 0, 1500);

        DPose loose = world.PoseIn(pen, cabin),
            held = world.PoseIn(HeldCopies().Should().ContainSingle().Which, cabin);
        Double3.Distance(held.Position, loose.Position).Should().BeLessThan(0.01);
        (loose.Rotation.AngularVelocityTo(held.Rotation, 1).Length * 180 / Math.PI).Should().BeLessThan(1);
    }

    /// <summary>
    /// The person floats face-down below the pen, which floats above its back. Its hand, under its chest, lines up behind
    /// the pen first and then closes in on it from in front, so the body never comes within reach of touching the pen, and
    /// the pen keeps the velocity of the clip's push until the take.
    /// </summary>
    [Fact]
    public void ThePassengerTakesThePenWithoutItsBodyTouchingThePen()
    {
        Board(passenger: true);
        TickUntil(world, () => ClipOf().State.Value is Released, 120);
        Tick(world, 2);
        double nearest = double.PositiveInfinity;

        TickUntil(
            world,
            () =>
            {
                if (!world.IsAlive(pen))
                    return true;
                nearest = Math.Min(nearest, PenFromBodyAxis());
                (world.VelocityIn(pen, cabin) - DemoSurface.PenPush)
                    .Length.Should()
                    .BeLessThan(1e-3, "nothing touches the pen");
                return false;
            },
            1500
        );

        world.IsAlive(pen).Should().BeFalse("the passenger took the pen");
        nearest.Should().BeGreaterThan(0.3 + DemoSurface.PenHalf.Z);
    }

    /// <summary>Every live entity with a <see cref="HeldInView"/>: the drawn copies of a pen in a hand.</summary>
    private List<Entity> HeldCopies()
    {
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        return [.. live.Where(entity => world.GetComponent<HeldInView>(entity) != null)];
    }
}
