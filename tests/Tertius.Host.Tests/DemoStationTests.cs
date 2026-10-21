using System.Numerics;
using Tertius.Demo;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The station of the Blue Danube demo in the scene <c>demo-surface</c>, built without a script, on the World and the real
/// Jolt backend (docs/plans/blue-danube-demo.md, "The station" and "The station's plane"): a wheel 202 m across on a
/// circle 400 km up in <c>earth</c>, which does not turn, in the launch plane, that turns about its axis at the rate that
/// gives one gravity on its floor. In the station's own axes, +Y is the axis and the slot faces +Y. In <c>earth</c>, the
/// axis points along the launch plane's normal, reversed, the departure axes' +X. The pure tests pin the plane and the
/// date that put the ramp in the station's plane at the departure.
/// </summary>
public sealed class DemoStationTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();
    private readonly SceneInstance scene;
    private readonly Entity station;
    private readonly FrameHandle earth, frame;

    public DemoStationTests()
    {
        scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        station = scene["station"];
        earth = world.GetOwnedFrame(scene["earth"]);
        frame = world.GetOwnedFrame(station);
    }

    public void Dispose() => world.Dispose();

    /// <summary>The angle between two directions, in degrees.</summary>
    private static double Degrees(Double3 a, Double3 b) => Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1)) * 180 / Math.PI;

    /// <summary>The ground's rotation in earth at the departure, by the demo's date: the departure axes.</summary>
    private static DQuaternion DepartureAxes => DemoSurface.Date.Ground.RotationAt(DemoSurface.DepartureAt);

    /// <summary>The prograde normal of the station's orbit, in earth's axes: its position across its velocity.</summary>
    private static Double3 OrbitNormal
    {
        get
        {
            (Double3 place, Double3 velocity) = DemoStation.Orbit.StateAt(0, DemoScenes.EarthMu);
            return Double3.Cross(place, velocity).Normalized();
        }
    }

    /// <summary>
    /// At the departure, the ramp's incline, turned by the date's ground into earth's axes, lies in the plane of the
    /// station's orbit, within 1e-9, and its part across the pad's up points the way that the station moves over the pole at
    /// <see cref="DemoStation.OverPadAt"/>, within 1e-6 rad.
    /// </summary>
    [Fact]
    public void AtTheDepartureTheRampLiesInTheStationsPlaneAlongItsMotion()
    {
        Double3 incline = DepartureAxes.Rotate(DemoScenes.LaunchRamp.Up);
        (Double3 place, Double3 velocity) = DemoStation.Orbit.StateAt(DemoStation.OverPadAt, DemoScenes.EarthMu);
        Double3 up = place.Normalized();

        Math.Abs(Double3.Dot(incline, OrbitNormal)).Should().BeLessThan(1e-9, $"the incline {incline} lies in the station's plane");
        Double3 across = incline - up * Double3.Dot(incline, up);
        (Degrees(across, velocity) * Math.PI / 180).Should().BeLessThan(1e-6, $"the incline points along the station's motion {velocity}");
    }

    /// <summary>The launch plane's normal, in earth's axes, is the prograde normal of the station's orbit, within 1e-9.</summary>
    [Fact]
    public void TheLaunchPlaneNormalIsTheStationsOrbitNormal()
    {
        (DemoSurface.LaunchPlaneNormal - OrbitNormal).Length.Should().BeLessThan(1e-9, $"the launch plane's normal {DemoSurface.LaunchPlaneNormal} is the orbit's {OrbitNormal}");
    }

    /// <summary>
    /// At the world's time 0, the Sun's direction from Earth lies 56.1 degrees from the station's axis on the slot's side,
    /// within 0.5 degrees. The plane of the orbit was chosen for that light (docs/plans/blue-danube-demo.md, "The station's
    /// plane"): the sun meets the slot's face at an angle, neither edge-on nor square.
    /// </summary>
    [Fact]
    public void TheSunLightsTheSlotsFaceAt56DegreesFromTheAxis()
    {
        KeplerOrbit earthOrbit = DemoScenes.EarthOrbit(DemoSurface.Date);
        Double3 toSun = earthOrbit.RotationAt(0, DemoScenes.SunMu).Conjugate().Rotate(-earthOrbit.StateAt(0, DemoScenes.SunMu).Position);
        Double3 axis = DemoStation.Orbit.RotationAt(0, DemoScenes.EarthMu).Rotate(Double3.UnitY);

        Degrees(axis, toSun).Should().BeApproximately(56.1, 0.5);
    }

    /// <summary>
    /// Over the first 10 s, sampled each second, the station's center lies 400 km above Earth's mean radius in earth, in the
    /// launch plane, and moves in it at the speed of a circle there.
    /// </summary>
    [Fact]
    public void TheStationOrbitsInTheLaunchPlane400KmUp()
    {
        double radius = DemoScenes.EarthRadius + 400e3;
        for (int second = 0; second < 10; second++)
        {
            Tick(world, 60);
            Double3 place = world.PoseIn(station, earth).Position, velocity = world.VelocityIn(station, earth);
            place.Length.Should().BeApproximately(radius, 0.01, $"the station is 400 km up after {second + 1} s");
            Double3.Dot(place, DemoSurface.LaunchPlaneNormal).Should().BeApproximately(0, 0.01, $"the station is in the launch plane after {second + 1} s");
            Double3.Dot(velocity, DemoSurface.LaunchPlaneNormal).Should().BeApproximately(0, 1e-5, $"the station moves in the launch plane after {second + 1} s");
            velocity.Length.Should().BeApproximately(Math.Sqrt(DemoScenes.EarthMu / radius), 1e-5);
        }
    }

    /// <summary>
    /// At <see cref="DemoStation.OverPadAt"/>, the station's orbit passes straight over the pad, along +Y of earth, and moves
    /// along the departure axes' -Z, the way the ramp sends the shuttle.
    /// </summary>
    [Fact]
    public void TheStationPassesOverThePadAtOverPadAtMovingTheWayOfTheClimb()
    {
        (Double3 place, Double3 velocity) = DemoStation.Orbit.StateAt(DemoStation.OverPadAt, DemoScenes.EarthMu);
        Double3 climb = DepartureAxes.Rotate(-Double3.UnitZ);

        (place.Normalized() - Double3.UnitY).Length.Should().BeLessThan(1e-9, $"the station is over the pad, not at {place}");
        (velocity.Normalized() - climb).Length.Should().BeLessThan(1e-9, $"the station moves along {climb}, not along {velocity}");
    }

    /// <summary>
    /// The station turns about its axis, the launch plane's normal reversed, at <see cref="DemoScenes.SpinStation.Rate"/>,
    /// in earth, and so a body at rest on the floor, 100 m from the axis, feels 9.81 m/s² away from the axis.
    /// </summary>
    [Fact]
    public void TheStationTurnsAboutItsAxisAtTheRateThatGivesOneGravityOnTheFloor()
    {
        Tick(world, 3);

        Double3 axis = -DemoSurface.LaunchPlaneNormal;
        Vector3 turn = world.AngularVelocityIn(station, earth);
        ((Double3)turn - axis * DemoScenes.SpinStation.Rate).Length.Should().BeLessThan(1e-6, $"the station turns about {axis}, not at {turn}");
        (world.PoseIn(station, earth).Rotation.Rotate(Double3.UnitY) - axis).Length.Should().BeLessThan(1e-9, "the station's axis points along the launch plane's normal, reversed");
        Double3 felt = world.Frames.FeltGravity(frame, new Double3(0, 0, -100));
        (felt - new Double3(0, 0, -9.81)).Length.Should().BeLessThan(0.005, $"the floor has one gravity away from the axis, not {felt}");
    }

    /// <summary>
    /// The station is a Kepler frame in earth, which does not turn and falls freely round the Sun, so it falls freely: its
    /// proper acceleration is zero in every step, from the first on, because its spawn carries the first motion intent of
    /// its orbit.
    /// </summary>
    [Fact]
    public void TheStationFallsFreelyFromItsFirstStep()
    {
        world.GetFrame(station).Should().Be(earth);
        for (int tick = 1; tick <= 120; tick++)
        {
            world.Tick();
            world.Frames.ProperAcceleration(frame).Length().Should().BeLessThan(1e-6f, $"the station falls freely in tick {tick}");
        }
    }

    /// <summary>
    /// The deck, the bay's wall at x = -8 m, holds a box that drops onto it from 0.75 m, and pulls it with the weak spin
    /// gravity of its radius, ω² · 8 m = 0.785 m/s², about 8 percent of a gravity. The box falls in about 1.4 s and rests on
    /// the deck after 8 s.
    /// </summary>
    [Fact]
    public void TheDeckHasTheWeakGravityOfItsRadius()
    {
        const float half = 0.25f;
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose(new Double3(-7, 0, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(half)), Mass: 10),
        });

        Tick(world, 8 * 60);

        double rate = DemoScenes.SpinStation.Rate;
        Double3 felt = world.Frames.FeltGravity(frame, new Double3(-8, 0, 0));
        (felt - new Double3(-rate * rate * 8, 0, 0)).Length.Should().BeLessThan(1e-3, $"the deck pulls with ω² · 8 m, not {felt}");
        world.GetFrame(box).Should().Be(frame);
        world.PoseIn(box, frame).Position.X.Should().BeApproximately(-8 + half, 0.02, "the box rests on the deck");
        world.VelocityIn(box, frame).Length.Should().BeLessThan(0.05, "the box rests on the deck");
    }

    /// <summary>
    /// A box of the shuttle's size, 6 m wide, 4 m tall and 16 m long, starts on the axis 9 m outside the hub's +Y face, at
    /// rest in the station's axes, with its height along the station's X and its length along the axis, as the shuttle
    /// enters. It moves inward at 1 m/s. In 18 s it passes through the slot, 6 by 8 m, and lies wholly in the bay, and it
    /// touches nothing on the way: on the axis, it feels no spin gravity and no Coriolis force.
    /// </summary>
    [Fact]
    public void ABoxOfTheShuttlesSizeFliesThroughTheSlotAlongTheAxisWithoutAContact()
    {
        var touches = new Touches();
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose(new Double3(0, 22, 0), AttitudeAutopilot.Facing(-Double3.UnitY, Double3.UnitX)),
            LinearVelocity = new Vector3(0, -1, 0),
            Body = new BodyDesc(new BoxShapeDesc(Shuttle.Hull), Mass: Shuttle.Mass, LinearDamping: 0, AngularDamping: 0),
            Components = [touches],
        });

        Tick(world, 18 * 60);

        touches.Count.Should().Be(0, "the box passes through the slot without touching the hub");
        world.GetFrame(box).Should().Be(frame);
        Double3 place = world.PoseIn(box, frame).Position;
        (place - new Double3(0, 4, 0)).Length.Should().BeLessThan(0.05, $"the box flies straight along the axis into the bay, not to {place}");
    }

    /// <summary>
    /// The station's bubble, a sphere of 120 m, holds its whole hull outside the frame, whose farthest point, a corner of the
    /// wheel's rim, lies 101.5 m from the center, and the drawn station too. So a body enters the frame before it reaches
    /// the hull.
    /// </summary>
    [Fact]
    public void TheStationsBubbleHoldsItsWholeHull()
    {
        FrameBounds bubble = world.Frames.Bounds(frame);
        double hull = DemoStation.HullShape().BoundingRadius();

        hull.Should().BeApproximately(Math.Sqrt(101 * 101 + 10 * 10), 0.01, "the wheel's rim is the hull's farthest point");
        bubble.MinRadius.Should().BeGreaterThan((float)hull + 18, "the bubble holds the hull with room to spare");
        bubble.MinRadius.Should().BeGreaterThan(world.GetRender(station).BoundingRadius, "the bubble holds the drawn station");
    }

    /// <summary>The shuttle's bubble is less than half the station's, so the shuttle's frame fits in the station's.</summary>
    [Fact]
    public void TheShuttlesFrameFitsInTheStations()
    {
        FrameBounds.Fits(world.Frames.Bounds(world.GetOwnedFrame(scene["shuttle"])), world.Frames.Bounds(frame)).Should().BeTrue();
    }

    /// <summary>
    /// On the ground, the parked shuttle's rendezvous autopilot halts at a <see cref="Rendezvous"/> order: the shuttle is in
    /// the turning earth-surface, and the station is in earth, so its guard finds the station outside the shuttle's frame
    /// (docs/plans/blue-danube-demo.md, "The shuttle's flight in the turning ground"). In the run the order comes in orbit,
    /// after the hand-off into earth, and the autopilot flies (<see cref="BlueDanubeRendezvousTests"/>).
    /// </summary>
    [Fact]
    public void OnTheGroundTheShuttlesRendezvousAutopilotHaltsBecauseTheStationIsInEarth()
    {
        world.Post(Event.Create(scene["shuttle"], Entity.Null, new Rendezvous()));

        Tick(world, 2);

        object state = world.GetComponent<RendezvousAutopilot>(scene["shuttle"])!.State.Value;
        state.Should().BeOfType<Halted>();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A free shuttle at <see cref="DemoStation.HoldPoint"/>, 150 m out on the axis, with the station's velocity and its nose
    /// at the slot, drifts less than a meter in 60 s and stays in earth all the while: its bounding sphere of about 9 m lies
    /// 21 m outside the station's bubble.
    /// </summary>
    [Fact]
    public void AShuttleAtTheHoldPointStaysInEarth()
    {
        Tick(world, 1);
        DPose pose = world.PoseIn(station, earth);
        Double3 axis = pose.Rotation.Rotate(Double3.UnitY);
        Double3 hold = pose.TransformPoint(DemoStation.HoldPoint);
        Entity shuttle = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = new DPose(hold, AttitudeAutopilot.Facing(-axis, hold)),
            LinearVelocity = world.VelocityIn(station, earth),
            Body = Shuttle.Body(BodyMotion.Dynamic),
            AsFrame = new FrameSpec(new FrameBounds(Shuttle.Bubble)),
        });

        for (int tick = 0; tick < 60 * 60; tick++)
        {
            world.Tick();
            world.GetFrame(shuttle).Should().Be(earth, $"the shuttle stays in earth in tick {tick}");
        }
        double distance = (world.PoseIn(shuttle, earth).Position - world.PoseIn(station, earth).Position).Length;
        distance.Should().BeApproximately(DemoStation.HoldPoint.Length, 1, "the shuttle drifts little from the hold point");
    }

    /// <summary>A test component that counts the contacts that start on its entity.</summary>
    private sealed class Touches : IComponent
    {
        public int Count { get; private set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Collision>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref Collision _ in events.Of<Collision>())
                Count++;
        }
    }
}
