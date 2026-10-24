using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Testing;

/// <summary>
/// Scenes that several tests start from, built in a <see cref="World"/> that runs on the real physics. Each builder spawns
/// its entities and frames before the first tick and returns the handles that a test needs. A test adds to the scene
/// after it, and ticks.
/// </summary>
public static class Scenes
{
    /// <summary>The G·M of the Earth, in m³/s².</summary>
    public const double EarthMu = 3.986004418e14;

    /// <summary>
    /// How far below the origin of a ship's frame the deck of <see cref="ShipWithInterior"/> has its top, in meters. A body
    /// at rest on the deck has its lowest point there.
    /// </summary>
    public const double DeckTop = -2;

    /// <summary>
    /// The G·M of <see cref="AsteroidSpace"/>, in m³/s². A circular orbit of 5 km in it moves at 2 km/s, twice the speed of
    /// 1 km/s from which a hull on rails gets a following frame of its own (a Kepler hull never thrusts, so it needs the speed
    /// of a group of free bodies, see <see cref="NumericFrameOptions.CreateSpeed"/>). A body at rest 5 km out lies far nearer
    /// to the origin than the 8,389 km at which it gets a numeric frame, so it stays in the system of space. The pull there
    /// is 800 m/s², and a hull on the orbit moves 33 m in a step of 1/60 s.
    /// </summary>
    public const double AsteroidMu = 2e10;

    /// <summary>A ground frame, its floor and a ball above the floor.</summary>
    /// <param name="Ground">The root frame, with uniform gravity along -Y.</param>
    /// <param name="Floor">The static floor, whose top is at height 0 in <paramref name="Ground"/>.</param>
    /// <param name="Ball">The dynamic ball, released at rest above the floor.</param>
    public readonly record struct FloorAndBall(FrameHandle Ground, Entity Floor, Entity Ball);

    /// <summary>
    /// A root frame with a gravity of <paramref name="g"/> m/s² along -Y, a floor with its top at height 0, and a ball
    /// released at rest <paramref name="height"/> m above the floor, measured to the bottom of the ball. The ball has
    /// <paramref name="ballComponents"/>.
    /// </summary>
    public static FloorAndBall FloorWithBall(
        World world,
        float g = 9.81f,
        double height = 2,
        params IComponent[] ballComponents
    )
    {
        FrameHandle ground = Ground(world, g);
        Entity floor = Floor(world, ground);
        Entity ball = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, height + 0.5),
                Body = Ball(),
                Components = ballComponents,
            }
        );
        return new(ground, floor, ball);
    }

    /// <summary>A ship and its inside.</summary>
    /// <param name="Hull">The entity that owns the ship's frame. Its body is the hull, in the parent frame.</param>
    /// <param name="Interior">The ship's frame: its bubble, with the deck inside.</param>
    public readonly record struct ShipScene(Entity Hull, FrameHandle Interior);

    /// <summary>
    /// A ship in <paramref name="space"/>: a dynamic hull of 10 m by 6 m by 30 m at <paramref name="position"/>, and a frame
    /// whose bubble has a radius of <paramref name="bubbleRadius"/> m. The inside
    /// is a deck of 9 m by 0.5 m by 29 m, whose top is at <see cref="DeckTop"/> in the ship's frame. The ship has a
    /// uniform gravity of <paramref name="g"/> m/s² along -Y in its own frame, or none when <paramref name="g"/> is 0. The
    /// ship starts at rest.
    /// </summary>
    public static ShipScene ShipWithInterior(
        World world,
        FrameHandle space,
        Double3 position = default,
        float bubbleRadius = 50,
        float g = 0
    )
    {
        var deck = new CompoundShapeDesc([
            new(new BoxShapeDesc(new(4.5f, 0.25f, 14.5f)), new(new(0, (float)DeckTop - 0.25f, 0), Quaternion.Identity)),
        ]);
        GravityField gravity = g == 0 ? default : GravityField.Uniform(new(0, -g, 0));
        Entity hull = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(position.X, position.Y, position.Z),
                Body = new(
                    Shape: new BoxShapeDesc(new(5, 3, 15)),
                    Mass: 1000,
                    LinearDamping: 0,
                    AngularDamping: 0,
                    InteriorShape: deck
                ),
                AsFrame = new(FrameBounds.Sphere(bubbleRadius), gravity),
            }
        );
        return new(hull, world.GetOwnedFrame(hull));
    }

    /// <summary>A planet and a moon that goes round it.</summary>
    /// <param name="Planet">The root frame, with a point gravity at its origin.</param>
    /// <param name="Moon">The Kepler frame of the moon, a child of <paramref name="Planet"/>.</param>
    public readonly record struct MoonScene(FrameHandle Planet, FrameHandle Moon);

    /// <summary>
    /// A planet with a G·M of <paramref name="planetMu"/> m³/s² as the root frame, and a moon on a circular orbit of
    /// <paramref name="orbitRadius"/> m round it, with the moon's bubble at <paramref name="bubbleRadius"/> m. The moon
    /// starts at the angle <paramref name="phase"/> rad round the planet's +Y, at its place on the orbit at the world's time,
    /// and a <see cref="KeplerMotion"/> moves it from the second sync point after its spawn on (see <see cref="RailsSpawn"/>).
    /// The moon takes its G·M from the planet's point gravity, and so does every loose body in the planet's frame.
    /// </summary>
    public static MoonScene MoonOnKeplerOrbit(
        World world,
        double planetMu = EarthMu,
        double orbitRadius = 7e6,
        float bubbleRadius = 50,
        double phase = 0.7
    )
    {
        FrameHandle planet = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(planetMu)));
        var orbit = KeplerOrbit.Circular(orbitRadius, phase: phase);
        FrameHandle moon = world.GetOwnedFrame(
            world.Spawn(
                new SpawnDesc { Frame = planet, AsFrame = new(FrameBounds.Sphere(bubbleRadius)) }.OnKeplerOrbit(
                    world,
                    orbit
                )
            )
        );
        return new(planet, moon);
    }

    /// <summary>A root frame with a point gravity of <see cref="AsteroidMu"/>, so that a frame in it can follow a Kepler orbit.</summary>
    public static FrameHandle AsteroidSpace(World world) =>
        world.CreateRootFrame(new(FrameBounds.Sphere(1e7f), GravityField.Point(AsteroidMu)));

    /// <summary>
    /// The owner of a frame on a Kepler orbit in <paramref name="space"/>, an <see cref="AsteroidSpace"/>, whose
    /// <see cref="KeplerMotion"/> moves it on a circular orbit of 5 km at phase 0 (see <see cref="RailsSpawn"/>): its hull, of
    /// <paramref name="body"/>, starts on the orbit at the world's time, at (5000, 0, 0) at time 0, and moves along -Z at 2 km/s,
    /// so it lives in a following frame of its own (docs/plans/numeric-frames.md, "Anchors", rule 3). Its bubble is a sphere
    /// of 1 cm, so that no body near the hull enters it. A static body that the hull passes is an anchor at rest, which no
    /// home shares with the hull: a group that links the two lives in the static body's home, and that home keeps a ghost
    /// of the hull. The spawn carries the orbit's sample of the first step as its first motion intent, so the hull is on
    /// its orbit from its first step. Without it, the hull would hold still for its first step, in its following frame too
    /// (ticket ter-zsku), and get its following frame again only after its first sample.
    /// </summary>
    public static Entity FastKeplerHull(
        World world,
        FrameHandle space,
        BodyDesc body,
        params IComponent[] components
    ) =>
        world.Spawn(
            new SpawnDesc
            {
                Frame = space,
                Body = body,
                AsFrame = new(FrameBounds.Sphere(0.01f)),
                Components = components,
            }.OnKeplerOrbit(world, KeplerOrbit.Circular(5_000), firstMove: true)
        );

    /// <summary>Where a ball starts and how fast it flies, so that it enters a bubble in a chosen tick. See
    /// <see cref="BallApproachingBubble"/>.</summary>
    /// <param name="Pose">The pose of the ball in the frame of the bubble's parent, in meters.</param>
    /// <param name="Velocity">The velocity of the ball in that frame, in meters per second.</param>
    public readonly record struct Approach(DPose Pose, Double3 Velocity);

    /// <summary>
    /// The start of a straight flight along +X into the bubble of the frame <paramref name="into"/>, which must be a child of
    /// the frame that the ball starts in, with no turn, and the bubble of a sphere. The ball flies at <paramref name="speed"/>
    /// m/s in a parent frame without gravity, along a line that is <paramref name="lateral"/> m to the +Y side of the
    /// center of the bubble. Jolt hands a body to a child frame in the first step that ends with the body touching the
    /// bubble (the distance of its center from the bubble's surface is at most its radius). The flight is timed so that the
    /// step of tick <paramref name="tick"/> is that step, with half a step of travel as margin on both sides: the step before
    /// ends half a step short of touching, and the step of the hand-off ends half a step past it. Tick 0 is the first tick
    /// of the world. Two balls with different <paramref name="lateral"/> values fly side by side without touching.
    /// </summary>
    /// <param name="ballRadius">The radius of the ball in meters. 0.5 is the radius of <see cref="Ball"/>.</param>
    public static Approach ApproachToBubble(
        World world,
        FrameHandle into,
        long tick,
        float ballRadius = 0.5f,
        double speed = 12,
        double lateral = 0
    )
    {
        DPose bubble = world.Frames.LocalPose(into);
        double metersPerStep = speed * world.DeltaTime;
        double reach = world.Frames.Bounds(into).MaxRadius + ballRadius;
        double centerWhenTouching = bubble.Position.X - Math.Sqrt(reach * reach - lateral * lateral);
        double start = centerWhenTouching + metersPerStep / 2 - (tick + 1) * metersPerStep;
        return new(At(start, bubble.Position.Y + lateral, bubble.Position.Z), new(speed, 0, 0));
    }

    /// <summary>
    /// Spawns a ball of <see cref="Ball"/> in <paramref name="from"/> that Jolt hands off into the bubble of
    /// <paramref name="into"/> in the step of tick <paramref name="tick"/>, and in no step before it (see
    /// <see cref="ApproachToBubble"/>
    /// for the conditions). The ball has <paramref name="components"/>.
    /// </summary>
    public static Entity BallApproachingBubble(
        World world,
        FrameHandle from,
        FrameHandle into,
        long tick,
        params IComponent[] components
    )
    {
        Approach approach = ApproachToBubble(world: world, into: into, tick: tick);
        return world.Spawn(
            new()
            {
                Frame = from,
                Pose = approach.Pose,
                LinearVelocity = approach.Velocity,
                Body = Ball(),
                Components = components,
            }
        );
    }
}
