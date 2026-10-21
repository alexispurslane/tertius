using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The station of the Blue Danube demo in the scene <c>demo-surface</c>, built without a script, on the World and the real
/// Jolt backend (docs/plans/blue-danube-demo.md, "The station"): a wheel 202 m across on a circle 400 km up, in the launch
/// plane, that turns about its axis at the rate that gives one gravity on its floor. In the station's own axes, +Y is the
/// axis and the slot faces +Y. In <c>earth-surface</c>, the axis points along +X.
/// </summary>
public sealed class DemoStationTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();
    private readonly SceneInstance scene;
    private readonly Entity station;
    private readonly FrameHandle surface, frame;

    public DemoStationTests()
    {
        scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        station = scene["station"];
        surface = world.GetOwnedFrame(scene["earth-surface"]);
        frame = world.GetOwnedFrame(station);
    }

    public void Dispose() => world.Dispose();

    /// <summary>
    /// The launch plane's normal, as the design defines it: the pad's up, +Y of <c>earth-surface</c>, across the ramp's
    /// incline, normalized.
    /// </summary>
    private static Double3 LaunchPlaneNormal => Double3.Cross(Double3.UnitY, DemoScenes.LaunchRamp.Up).Normalized();

    /// <summary>
    /// Over the first 10 s, sampled each second, the station's center lies 400 km above Earth's mean radius, in the launch
    /// plane, and moves in it at the speed of a circle there.
    /// </summary>
    [Fact]
    public void TheStationOrbitsInTheLaunchPlane400KmUp()
    {
        double radius = DemoScenes.EarthRadius + 400e3;
        for (int second = 0; second < 10; second++)
        {
            Tick(world, 60);
            Double3 place = world.PoseIn(station, surface).Position, velocity = world.VelocityIn(station, surface);
            place.Length.Should().BeApproximately(radius, 0.01, $"the station is 400 km up after {second + 1} s");
            Double3.Dot(place, LaunchPlaneNormal).Should().BeApproximately(0, 0.01, $"the station is in the launch plane after {second + 1} s");
            Double3.Dot(velocity, LaunchPlaneNormal).Should().BeApproximately(0, 1e-5, $"the station moves in the launch plane after {second + 1} s");
            velocity.Length.Should().BeApproximately(Math.Sqrt(DemoScenes.EarthMu / radius), 1e-5);
        }
    }

    /// <summary>
    /// At <see cref="DemoStation.OverPadAt"/>, the station's orbit passes straight over the pad, along +Y of
    /// <c>earth-surface</c>, and moves toward -Z, the way the ramp sends the shuttle.
    /// </summary>
    [Fact]
    public void TheStationPassesOverThePadAtOverPadAtMovingTheWayOfTheClimb()
    {
        (Double3 place, Double3 velocity) = DemoStation.Orbit.StateAt(DemoStation.OverPadAt, DemoScenes.EarthMu);

        (place.Normalized() - Double3.UnitY).Length.Should().BeLessThan(1e-9, $"the station is over the pad, not at {place}");
        (velocity.Normalized() + Double3.UnitZ).Length.Should().BeLessThan(1e-9, $"the station moves along -Z, not along {velocity}");
    }

    /// <summary>
    /// The station turns about its axis, +X of <c>earth-surface</c>, at <see cref="DemoScenes.SpinStation.Rate"/>, and so a
    /// body at rest on the floor, 100 m from the axis, feels 9.81 m/s² away from the axis.
    /// </summary>
    [Fact]
    public void TheStationTurnsAboutItsAxisAtTheRateThatGivesOneGravityOnTheFloor()
    {
        Tick(world, 3);

        Vector3 turn = world.AngularVelocityIn(station, surface);
        ((Double3)turn - Double3.UnitX * DemoScenes.SpinStation.Rate).Length.Should().BeLessThan(1e-6, $"the station turns about +X, not at {turn}");
        world.PoseIn(station, surface).Rotation.Rotate(Double3.UnitY).X.Should().BeApproximately(1, 1e-9, "the station's axis points along +X of earth-surface");
        Double3 felt = world.Frames.FeltGravity(frame, new Double3(0, 0, -100));
        (felt - new Double3(0, 0, -9.81)).Length.Should().BeLessThan(0.005, $"the floor has one gravity away from the axis, not {felt}");
    }

    /// <summary>
    /// The station is a Kepler frame in <c>earth-surface</c>, which does not turn, so it falls freely: its proper
    /// acceleration is zero in every step, from the first on, because its spawn carries the first motion intent of its
    /// orbit.
    /// </summary>
    [Fact]
    public void TheStationFallsFreelyFromItsFirstStep()
    {
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
    /// The parked shuttle's rendezvous autopilot flies to the scene's station: after a <see cref="Rendezvous"/> order, it
    /// leaves its standby and does not halt, so it has a live station in its own frame and Earth's point gravity.
    /// </summary>
    [Fact]
    public void TheShuttlesRendezvousAutopilotFliesToTheStation()
    {
        world.Post(Event.Create(scene["shuttle"], Entity.Null, new Rendezvous()));

        Tick(world, 2);

        object state = world.GetComponent<RendezvousAutopilot>(scene["shuttle"])!.State.Value;
        (state is Phasing or Transfer).Should().BeTrue($"the autopilot flies, not {state}");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The parked shuttle's docking autopilot docks in the scene's station: after a <see cref="Dock"/> order, it leaves its
    /// standby and waits for the hold, and does not halt, so it has a live station that owns a frame.
    /// </summary>
    [Fact]
    public void TheShuttlesDockingAutopilotDocksInTheStation()
    {
        world.Post(Event.Create(scene["shuttle"], Entity.Null, new Dock()));

        Tick(world, 2);

        world.GetComponent<DockingAutopilot>(scene["shuttle"])!.State.Value.Should().BeOfType<Waiting>();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// <c>bay-camera</c>, a still view in the station's frame near the bay's far wall, sees the whole docking: the corners of
    /// the slot on both faces of its wall, the corners of the parked shuttle's box and the corners of the spoke's mouth in
    /// the deck all lie in its picture.
    /// </summary>
    [Fact]
    public void TheBayCameraSeesTheSlotTheParkedHullAndTheSpokeMouth()
    {
        DPose view = world.PoseIn(scene["bay-camera"], frame);
        BoundingBox slot = DemoStation.Slot, parked = DemoStation.ParkedBounds, mouth = DemoStation.SpokeMouth;
        IEnumerable<Double3> Corners(BoundingBox box) => Enumerable.Range(0, 8).Select(corner => new Double3(
            (corner & 1) == 0 ? box.Min.X : box.Max.X, (corner & 2) == 0 ? box.Min.Y : box.Max.Y, (corner & 4) == 0 ? box.Min.Z : box.Max.Z));
        Double3[] seen = [.. Corners(slot), .. Corners(parked), .. Corners(mouth with { Min = mouth.Min with { X = DemoStation.Deck } })];

        foreach (Double3 corner in seen)
            Picture.Contains(Picture.Project(view, corner, world.VerticalFov)).Should().BeTrue($"the corner {corner} lies in the picture");
    }

    /// <summary>
    /// A free shuttle at <see cref="DemoStation.HoldPoint"/>, 150 m out on the axis, with the station's velocity and its nose
    /// at the slot, drifts less than a meter in 60 s and stays in <c>earth-surface</c> all the while: its bounding sphere of
    /// 8.8 m lies 21 m outside the station's bubble.
    /// </summary>
    [Fact]
    public void AShuttleAtTheHoldPointStaysInEarthSurface()
    {
        Tick(world, 1);
        DPose pose = world.PoseIn(station, surface);
        Double3 axis = pose.Rotation.Rotate(Double3.UnitY);
        Double3 hold = pose.TransformPoint(DemoStation.HoldPoint);
        Entity shuttle = world.Spawn(new SpawnDesc
        {
            Frame = surface,
            Pose = new DPose(hold, AttitudeAutopilot.Facing(-axis, hold)),
            LinearVelocity = world.VelocityIn(station, surface),
            Body = Shuttle.Body(BodyMotion.Dynamic),
            AsFrame = new FrameSpec(new FrameBounds(Shuttle.Bubble)),
        });

        for (int tick = 0; tick < 60 * 60; tick++)
        {
            world.Tick();
            world.GetFrame(shuttle).Should().Be(surface, $"the shuttle stays in earth-surface in tick {tick}");
        }
        double distance = (world.PoseIn(shuttle, surface).Position - world.PoseIn(station, surface).Position).Length;
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
