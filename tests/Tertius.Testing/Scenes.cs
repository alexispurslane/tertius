using System.Numerics;
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
    public static FloorAndBall FloorWithBall(World world, float g = 9.81f, double height = 2, params IComponent[] ballComponents)
    {
        FrameHandle ground = Ground(world, g);
        Entity floor = Floor(world, ground);
        Entity ball = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, height + 0.5), Body = Ball(), Components = ballComponents });
        return new FloorAndBall(ground, floor, ball);
    }

    /// <summary>A ship and its inside.</summary>
    /// <param name="Hull">The entity that owns the ship's frame. Its body is the hull, in the parent frame.</param>
    /// <param name="Interior">The ship's frame: its bubble, with the deck inside.</param>
    public readonly record struct ShipScene(Entity Hull, FrameHandle Interior);

    /// <summary>
    /// How far below the origin of a ship's frame the deck of <see cref="ShipWithInterior"/> has its top, in meters. A body
    /// at rest on the deck has its lowest point there.
    /// </summary>
    public const double DeckTop = -2;

    /// <summary>
    /// A ship in <paramref name="space"/>: a dynamic hull of 10 m by 6 m by 30 m at <paramref name="position"/>, and a frame
    /// whose bubble has a radius of <paramref name="bubbleRadius"/> m and whose driver is the physics driver. The inside
    /// is a deck of 9 m by 0.5 m by 29 m, whose top is at <see cref="DeckTop"/> in the ship's frame. The ship has a
    /// uniform gravity of <paramref name="g"/> m/s² along -Y in its own frame, or none when <paramref name="g"/> is 0. The
    /// ship starts at rest.
    /// </summary>
    public static ShipScene ShipWithInterior(World world, FrameHandle space, Double3 position = default, float bubbleRadius = 50, float g = 0)
    {
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(4.5f, 0.25f, 14.5f)), new Pose(new Vector3(0, (float)DeckTop - 0.25f, 0), Quaternion.Identity))]);
        GravityField gravity = g == 0 ? default : GravityField.Uniform(new Vector3(0, -g, 0));
        Entity hull = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(position.X, position.Y, position.Z),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 15)), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: deck),
            AsFrame = new FrameSpec(FrameBounds.Sphere(bubbleRadius), MotionDriver.Physics, gravity),
        });
        return new ShipScene(hull, world.GetOwnedFrame(hull));
    }

    /// <summary>A planet and a moon that goes round it.</summary>
    /// <param name="Planet">The root frame, with a point gravity at its origin.</param>
    /// <param name="Moon">The Kepler frame of the moon, a child of <paramref name="Planet"/>.</param>
    public readonly record struct MoonScene(FrameHandle Planet, FrameHandle Moon);

    /// <summary>
    /// A planet with a G·M of <paramref name="planetMu"/> m³/s² as the root frame, and a moon on a circular orbit of
    /// <paramref name="orbitRadius"/> m round it, with the moon's bubble at <paramref name="bubbleRadius"/> m. The moon
    /// starts at the angle <paramref name="phase"/> rad round the planet's +Y. The moon takes its G·M from the planet's
    /// point gravity, and so does every loose body in the planet's frame.
    /// </summary>
    public static MoonScene MoonOnKeplerOrbit(World world, double planetMu = EarthMu, double orbitRadius = 7e6, float bubbleRadius = 50, double phase = 0.7)
    {
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(planetMu)));
        FrameHandle moon = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            AsFrame = new FrameSpec(FrameBounds.Sphere(bubbleRadius), MotionDriver.Kepler, Orbit: KeplerOrbit.Circular(orbitRadius, phase: phase)),
        }));
        return new MoonScene(planet, moon);
    }
}
