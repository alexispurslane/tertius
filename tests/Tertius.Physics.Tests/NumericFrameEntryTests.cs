using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A body whose physics home is a numeric frame enters the bubble of a semantic frame that is not a child of that numeric
/// frame (ticket ter-gsvv). A numeric frame has no bubble. A parked station is a frame that physics does not drive, so its
/// hull stays in the system of the station's parent as an anchor, and the station stays a child of that parent. A fast
/// body that flies into the station's bubble must still enter the station, or it misses everything inside. These tests run
/// the World on the real backend with the default numeric frame options, under which a group with a body that thrusts gets
/// a falling numeric frame from 50 m/s relative to its frame, and a group of free bodies only from 1,000 m/s. A character
/// thrusts always. Any other body thrusts after 0.5 s (30 ticks) of thrust, so each fast ball and ship here carries a
/// gentle thruster (<see cref="Thruster"/>) along its flight and flies for 30 ticks before its falling frame matters.
/// </summary>
public class NumericFrameEntryTests
{
    private const float Dt = 1f / 60f;

    /// <summary>
    /// How many ticks of thrust at 60 Hz a body needs before it thrusts: the first count of ticks of 1/60 s that reaches the
    /// 0.5 s of <see cref="NumericFrameOptions.ThrustOnset"/>.
    /// </summary>
    private const int OnsetTicks = 30;

    /// <summary>
    /// The radius of the station's bubble, in meters. It is wider than the reach of the merge test between a body at
    /// 60 m/s and the station's hull, about 124 m, so the body still falls in its numeric frame when it enters. A body
    /// within that reach joins the system of the hull, an anchor (ter-dhkf).
    /// </summary>
    private const float StationRadius = 200;

    /// <summary>How far along +X from the station's center the crate floats, in meters: 25 m inside the bubble.</summary>
    private const double CrateX = 175;

    /// <summary>
    /// Where the bodies are along +X in space when their falling frame starts to matter, in meters: 60 m outside the
    /// station's bubble. They start <see cref="OnsetTicks"/> ticks of their 60 m/s farther out.
    /// </summary>
    private const double StartX = 1260;

    /// <summary>How far along +Y from the station's center the crate floats and the bodies fly, in meters, so that they miss the hull.</summary>
    private const double Lane = 10;

    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    /// <summary>A ball of radius 0.5 m and 1 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc Ball = new(new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0, LinearDamping: 0,
        AngularDamping: 0);

    private sealed record Scene(FrameHandle Space, FrameHandle Station, Entity Crate);

    /// <summary>
    /// Space, and 1 km along +X in it a parked station: a frame that physics does not drive, with a bubble of radius 200 m
    /// around a hull of 4 m by 4 m by 4 m at its center. The hull is an anchor in the system of space. A crate of
    /// 2 m by 2 m by 2 m and 2 kg floats in the station at (175, 10, 0).
    /// </summary>
    private static Scene ParkedStation(World world, BodyMotion hullMotion)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = space,
            Pose = At(1000),
            Body = new(new BoxShapeDesc(new(2)), hullMotion, Mass: 1e5f),
            AsFrame = new(FrameBounds.Sphere(StationRadius))
        }));
        Entity crate = world.Spawn(new()
        {
            Frame = station,
            Pose = At(CrateX, Lane),
            Body = new(new BoxShapeDesc(new(1)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0)
        });
        return new(space, station, crate);
    }

    /// <summary>
    /// Ticks until a step hands <paramref name="entity"/> off, at most <paramref name="limit"/> times. Returns the hand-off,
    /// or null. <paramref name="expected"/> is where the entity is in <paramref name="space"/> at the end of the last step if
    /// it coasts: its position before the last tick plus one step of its velocity then.
    /// </summary>
    private static HandOff? TickUntilHandedOff(World world, FaultInjectingBackend faults, Entity entity,
        FrameHandle space, int limit,
        out Double3 expected)
    {
        expected = default;
        for (int i = 0; i < limit; i++)
        {
            expected = world.PoseIn(entity, space).Position + world.VelocityIn(entity, space) * Dt;
            world.Tick();
            foreach (HandOff handOff in faults.Outputs[^1].HandOffs)
                if (handOff.Entity == entity)
                    return handOff;
        }

        return null;
    }

    /// <summary>True when some step so far reported that <paramref name="a"/> and <paramref name="b"/> started to touch.</summary>
    private static bool Touched(FaultInjectingBackend faults, Entity a, Entity b) =>
        faults.Outputs.SelectMany(output => output.Contacts).Any(c => (c.A == a && c.B == b) || (c.A == b && c.B == a));

    /// <summary>
    /// Flies <paramref name="body"/>, with <paramref name="components"/>, from (1290, 10, 0) in space at 60 m/s along -X
    /// into the station. It checks that the body falls in a numeric frame after <see cref="OnsetTicks"/> ticks, at
    /// (1260, 10, 0), and then enters the station from that frame on the first touch of its bubble, with its pose and
    /// velocity relative to the station. A body that is not a character needs a thruster in
    /// <paramref name="components"/> to thrust by then.
    /// </summary>
    private static Entity FlyFastIntoTheStation(World world, FaultInjectingBackend faults, Scene scene, BodyDesc body,
        params IComponent[] components)
    {
        Entity flier = world.Spawn(new()
        {
            Frame = scene.Space,
            Pose = At(StartX + 60 * OnsetTicks * (double)Dt, Lane),
            LinearVelocity = new Vector3(-60, 0, 0),
            Body = body,
            Components = components
        });
        Tick(world, OnsetTicks);
        FrameHandle home = world.PhysicsHomeOf(flier);
        world.Frames.Numeric(home).Should().Be(NumericMode.Falling,
            "the body thrusts at 60 m/s, so it gets a falling numeric frame");
        world.Frames.TreeParent(home).Should().Be(scene.Space);

        HandOff? entry = TickUntilHandedOff(world, faults, flier, scene.Space, 120, out Double3 expected);

        entry.Should().NotBeNull("the body reaches the station's bubble within 2 s");
        entry!.Value.From.Should().Be(home);
        entry.Value.To.Should().Be(scene.Station);
        world.GetFrame(flier).Should().Be(scene.Station);
        float radius = body.Shape.BoundingRadius();
        (entry.Value.Pose.Position.Length - StationRadius).Should().BeInRange(radius - 60 * Dt - 1e-3, radius,
            "the body enters on the first step that ends with its bounding sphere touching the bubble");
        (entry.Value.Pose.Position - (expected - new Double3(1000, 0, 0))).Length.Should().BeLessThan(1e-3,
            "the pose is where the body is at the end of the step, relative to the station");
        entry.Value.LinearVelocity.X.Should().BeApproximately(-60, 1e-3f);
        entry.Value.LinearVelocity.Y.Should().BeApproximately(0, 1e-3f);
        return flier;
    }

    [Theory]
    [InlineData(BodyMotion.Static)]
    public void AFastBallEntersAParkedStationAndHitsACrateInside(BodyMotion hullMotion)
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scene scene = ParkedStation(world, hullMotion);

        Entity ball = FlyFastIntoTheStation(world, faults, scene, Ball,
            new Thruster(Ball.Mass) { Direction = -Vector3.UnitX });
        TickUntil(world, () => Touched(faults, ball, scene.Crate), 60);
        Tick(world, 10);

        Touched(faults, ball, scene.Crate).Should().BeTrue("the ball reaches the crate about 0.4 s after it enters");
        world.VelocityIn(scene.Crate, scene.Station).X.Should().BeLessThan(-10, "the ball pushed the crate along");
        (world.PoseIn(ball, scene.Station).Position.X - world.PoseIn(scene.Crate, scene.Station).Position.X).Should()
            .BeGreaterThan(1,
                "the ball did not pass through the crate");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFastPersonEntersAParkedStationAndHitsACrateInside()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scene scene = ParkedStation(world, BodyMotion.Static);

        // A character thrusts always, so the person needs no thruster.
        Entity person = FlyFastIntoTheStation(world, faults, scene, Person);
        TickUntil(world, () => Touched(faults, person, scene.Crate), 60);
        Tick(world, 10);

        Touched(faults, person, scene.Crate).Should()
            .BeTrue("the person reaches the crate about 0.4 s after it enters");
        (world.PoseIn(person, scene.Station).Position.X - world.PoseIn(scene.Crate, scene.Station).Position.X).Should()
            .BeGreaterThan(1,
                "the person did not pass through the crate");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship that flies with <paramref name="velocity"/> from <paramref name="position"/> in space: a hull of 4 m by 4 m by
    /// 4 m and 1000 kg that physics drives, with a gentle thruster along its velocity, a bubble of
    /// <paramref name="bubbleRadius"/> m, and a crate of 2 m by 2 m by 2 m and 2 kg that floats in the ship at (0, 15, 0).
    /// </summary>
    private static (FrameHandle Space, FrameHandle Ship, Entity Crate) FastShip(World world, float bubbleRadius,
        Double3 position, Vector3 velocity)
    {
        const float hullMass = 1000;
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = space,
            Pose = new(position, DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = new(new BoxShapeDesc(new(2)), Mass: hullMass, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(bubbleRadius)),
            Components = [new Thruster(hullMass) { Direction = Vector3.Normalize(velocity) }]
        }));
        Entity crate = world.Spawn(new()
        {
            Frame = ship,
            Pose = At(0, 15),
            Body = new(new BoxShapeDesc(new(1)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0)
        });
        return (space, ship, crate);
    }

    [Fact]
    public void AFastBallInANumericFrameBesideTheShipsOwnEntersTheShipAndHitsACrateInside()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        (FrameHandle space, FrameHandle ship, Entity crate) = FastShip(world, 30, Double3.Zero, new(0, 0, 60));
        // The ball flies with the ship and closes on it at 6 m/s. The merge test reaches 12 m at that speed, so the ball
        // touches the ship's bubble while the two are still in numeric frames of their own. The ball and the ship thrust
        // alike along +Z, so the thrust does not change how they close. The ball starts 3 m farther out than the 40 m at
        // which it is when the two start to thrust, 30 ticks later.
        Entity ball = world.Spawn(new()
        {
            Frame = space,
            Pose = At(0, 40 + 6 * OnsetTicks * (double)Dt),
            LinearVelocity = new Vector3(0, -6, 60),
            Body = Ball,
            Components = [new Thruster(Ball.Mass) { Direction = Vector3.UnitZ }]
        });
        Tick(world, OnsetTicks);
        FrameHandle home = world.PhysicsHomeOf(ball);
        world.Frames.Numeric(home).Should().Be(NumericMode.Falling, "the ball thrusts at 60 m/s");
        world.Frames.Numeric(world.Frames.TreeParent(ship)).Should().Be(NumericMode.Falling,
            "the ship's hull falls in a numeric frame of its own");
        world.Frames.TreeParent(ship).Should().NotBe(home);

        HandOff? entry = TickUntilHandedOff(world, faults, ball, space, 120, out Double3 expected);

        entry.Should().NotBeNull("the ball reaches the ship's bubble within 2 s");
        entry!.Value.From.Should().Be(home);
        entry.Value.To.Should().Be(ship);
        world.GetFrame(ball).Should().Be(ship);
        (entry.Value.Pose.Position.Length - 30).Should().BeInRange(0.5 - 6 * Dt - 1e-3, 0.5,
            "the ball enters on the first touch of the bubble");
        (entry.Value.Pose.Position - world.PoseIn(ship.Entity, space).InverseTransformPoint(expected)).Length.Should()
            .BeLessThan(1e-3,
                "the pose is where the ball is at the end of the step, relative to the ship");
        entry.Value.LinearVelocity.Y.Should().BeApproximately(-6, 1e-3f, "the velocity is relative to the ship");
        entry.Value.LinearVelocity.Z.Should().BeApproximately(0, 1e-3f, "the velocity is relative to the ship");
        TickUntil(world, () => Touched(faults, ball, crate), 300);
        Tick(world, 10);
        Touched(faults, ball, crate).Should().BeTrue("the ball reaches the crate about 2.3 s after it enters");
        world.VelocityIn(crate, ship).Y.Should().BeLessThan(-1, "the ball pushed the crate along");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABallAtRestInSpaceEntersTheBubbleOfAFastShipOnItsFirstTouch()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        // A bubble of 200 m, wider than the 120 m that the merge test reaches at 60 m/s, so the ship's bubble reaches the
        // ball before the ball joins the ship's numeric frame. The ship's hull thrusts from its thirtieth tick on, when it
        // is 300 m from the ball's plane, so it starts 30 ticks of its 60 m/s farther out.
        (FrameHandle space, FrameHandle ship, _) =
            FastShip(world, 200, new(0, 0, -300 - 60 * OnsetTicks * (double)Dt), new(0, 0, 60));
        Entity ball = world.Spawn(new() { Frame = space, Pose = At(10), Body = Ball });
        Tick(world, OnsetTicks);
        world.PhysicsHomeOf(ball).Should().Be(space);
        world.Frames.Numeric(world.Frames.TreeParent(ship)).Should()
            .Be(NumericMode.Falling, "the ship's hull thrusts at 60 m/s");

        HandOff? entry = TickUntilHandedOff(world, faults, ball, space, 180, out Double3 expected);

        entry.Should().NotBeNull("the ship's bubble reaches the ball within 3 s");
        world.Frames.Numeric(world.Frames.TreeParent(ship)).Should().Be(NumericMode.Falling,
            "the ship's bubble reaches the ball from a falling frame");
        entry!.Value.From.Should().Be(space);
        entry.Value.To.Should().Be(ship);
        world.GetFrame(ball).Should().Be(ship);
        (entry.Value.Pose.Position.Length - 200).Should().BeInRange(0.5 - 60 * Dt - 1e-3, 0.5,
            "the ball enters on the first touch of the bubble");
        (entry.Value.Pose.Position - world.PoseIn(ship.Entity, space).InverseTransformPoint(expected)).Length.Should()
            .BeLessThan(1e-3,
                "the pose is where the ball is at the end of the step, relative to the ship");
        entry.Value.LinearVelocity.Z.Should().BeApproximately(-60, 1e-3f, "the velocity is relative to the ship");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ASlowBallEntersAParkedStationFromTheSystemOfSpaceAndHitsACrateInside()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        Scene scene = ParkedStation(world, BodyMotion.Static);
        Entity ball = world.Spawn(new()
        {
            Frame = scene.Space, Pose = At(1000 + StationRadius + 5, Lane), LinearVelocity = new Vector3(-5, 0, 0),
            Body = Ball
        });
        world.Tick();
        world.PhysicsHomeOf(ball).Should().Be(scene.Space, "at 5 m/s the ball gets no numeric frame");

        HandOff? entry = TickUntilHandedOff(world, faults, ball, scene.Space, 120, out _);

        entry.Should().NotBeNull("the ball reaches the station's bubble within 2 s");
        entry!.Value.From.Should().Be(scene.Space);
        entry.Value.To.Should().Be(scene.Station);
        world.GetFrame(ball).Should().Be(scene.Station);
        (entry.Value.Pose.Position.Length - StationRadius).Should().BeInRange(0.5 - 5 * Dt - 1e-3, 0.5);
        TickUntil(world, () => Touched(faults, ball, scene.Crate), 360);
        Tick(world, 10);
        Touched(faults, ball, scene.Crate).Should().BeTrue("the ball reaches the crate about 4.5 s after it enters");
        world.VelocityIn(scene.Crate, scene.Station).X.Should().BeLessThan(-1, "the ball pushed the crate along");
        world.Errors.Should().BeEmpty();
    }
}
