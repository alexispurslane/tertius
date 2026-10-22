using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A Kepler frame that faces its parent, on the World and the real Jolt backend: the frame keeps its belly toward the
/// planet over many ticks, its owner's row follows it, and its hull in the planet's system turns with it.
/// </summary>
public class WorldFacingFrameTests
{
    // A station 5 km from a planet's center that goes round in about 31 s, so 10 s of ticks turn it by 2 radians.
    private const double PlanetMu = 5e9;

    /// <summary>
    /// The G·M of a heavier planet, in m³/s², round which the station of <see cref="Station"/> moves at 2 km/s, twice the
    /// 1 km/s from which a hull on rails nearer than 8,389 km to its frame's origin gets a following frame of its own (see
    /// <see cref="NumericFrameOptions.CreateSpeed"/>). Round <see cref="PlanetMu"/> it moves at exactly that 1 km/s.
    /// </summary>
    private const double HeavyPlanetMu = 2e10;

    private static readonly KeplerOrbit Facing = KeplerOrbit.Circular(5000) with
    {
        Attitude = KeplerAttitude.FacesParent
    };

    /// <summary>A planet with a G·M of <paramref name="mu"/> m³/s², and the station's owner on <see cref="Facing"/> round it.</summary>
    private static (World World, JoltPhysicsBackend Backend, FrameHandle Planet, Entity Owner) Station(
        double mu = PlanetMu)
    {
        World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle planet = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f), GravityField.Point(mu)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = new(new BoxShapeDesc(new(5, 3, 15)), BodyMotion.Kinematic, Mass: 1000),
            AsFrame = new(FrameBounds.Sphere(50))
        }.OnKeplerOrbit(world, Facing));
        return (world, backend, planet, owner);
    }

    [Fact]
    public void AStationThatFacesItsPlanetKeepsItsBellyTowardItTickAfterTick()
    {
        (World world, _, _, Entity owner) = Station();
        using (world)
        {
            Tick(world, 600);

            DPose pose = world.PoseInOwnFrame(owner);
            Double3 belly = pose.Rotation.Rotate(new Double3(0, -1, 0));
            Double3 towardPlanet = -pose.Position.Normalized();
            Double3.Distance(belly, towardPlanet).Should().BeLessThan(1e-9);
            world.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void TheStationsOwnerTurnsAtTheOrbitsRateAboutItsNormal()
    {
        (World world, _, _, Entity owner) = Station();
        using (world)
        {
            Tick(world, 600);

            // A round orbit turns at its mean motion, sqrt(mu / a³) = 0.2 rad/s, about the orbit normal, +Y.
            world.AngularVelocityInOwnFrame(owner).Should().Be(new Vector3(0, 0.2f, 0));
        }
    }

    [Fact]
    public void OnAnOvalOrbitTheStationsOwnerTurnsAtTheRateOfItsPlaceOnTheOrbit()
    {
        World world = NewWorld(out _);
        using (world)
        {
            FrameHandle planet = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f), GravityField.Point(PlanetMu)));
            // Periapsis at t = 0, apoapsis at half the 31.4 s period.
            var oval = new KeplerOrbit(5000, 0.3, 0, 0, 0, 0, 0, DQuaternion.Identity, 0, 0,
                KeplerAttitude.FacesParent);
            Entity owner = world.Spawn(new SpawnDesc
            {
                Frame = planet,
                Body = new(new BoxShapeDesc(new(5, 3, 15)), BodyMotion.Kinematic, Mass: 1000),
                AsFrame = new(FrameBounds.Sphere(50))
            }.OnKeplerOrbit(world, oval));

            Tick(world, 942);

            // Near apoapsis, 6,500 m out, the turn rate is h / r² = sqrt(mu a (1 - e²)) / 6,500² = 0.112892 rad/s.
            world.AngularVelocityInOwnFrame(owner).Y.Should().BeApproximately(0.112892f, 1e-4f);
        }
    }

    /// <summary>
    /// Round a heavier planet (see <see cref="HeavyPlanetMu"/>), the station's hull moves at 2 km/s on its orbit, so it rests
    /// in a following frame of its own (ter-dhkf, docs/plans/numeric-frames.md, "Anchors", rule 3), which never rotates, and
    /// the hull turns there with the frame, at 0.4 rad/s. Before rule 3, the hull lived in the planet's own system.
    /// </summary>
    [Fact]
    public void TheStationsHullTurnsWithTheFrameInTheFollowingFrameWhereItRests()
    {
        (World world, JoltPhysicsBackend backend, FrameHandle planet, Entity owner) = Station(HeavyPlanetMu);
        using (world)
        {
            Tick(world, 600);

            FrameHandle home = world.PhysicsHomeOf(owner);
            world.Frames.Numeric(home).Should().Be(NumericMode.Following);
            Quaternion hull = backend.BodiesIn(home)
                .Single(facts =>
                    BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner))
                .Rotation;
            var frame = world.PoseInOwnFrame(owner).Rotation.ToQuaternion();
            MathF.Abs(Quaternion.Dot(hull, frame)).Should().BeGreaterThan(1 - 1e-5f,
                "the hull's rotation is the frame's, up to the sign of the quaternion");
            world.Errors.Should().BeEmpty();
        }
    }
}
