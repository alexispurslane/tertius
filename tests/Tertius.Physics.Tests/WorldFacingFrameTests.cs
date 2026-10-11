using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A Kepler frame that faces its parent, on the World and the real Jolt backend: the frame keeps its belly toward the
/// planet over many ticks, its owner's row follows it, and its hull in the planet's system turns with it.
/// </summary>
public class WorldFacingFrameTests
{
    // A station 5 km from a planet's center that goes round in about 31 s, so 10 s of ticks turn it by 2 radians.
    private const double PlanetMu = 5e9;
    private static readonly KeplerOrbit Facing = KeplerOrbit.Circular(5000, PlanetMu) with { Attitude = KeplerAttitude.FacesParent };

    private static (World World, JoltPhysicsBackend Backend, FrameHandle Planet, Entity Owner) Station()
    {
        World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, GravityField.Point(PlanetMu)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 15)), Mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Kepler, Orbit: Facing),
        });
        return (world, backend, planet, owner);
    }

    [Fact]
    public void AStationThatFacesItsPlanetKeepsItsBellyTowardItTickAfterTick()
    {
        (World world, _, _, Entity owner) = Station();
        using (world)
        {
            Tick(world, 600);

            DPose pose = world.GetPose(owner);
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
            world.GetAngularVelocity(owner).Should().Be(new Vector3(0, 0.2f, 0));
        }
    }

    [Fact]
    public void TheStationsHullInThePlanetsSystemTurnsWithTheFrame()
    {
        (World world, JoltPhysicsBackend backend, FrameHandle planet, Entity owner) = Station();
        using (world)
        {
            Tick(world, 600);

            Quaternion hull = backend.BodiesIn(planet)
                .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner)).Rotation;
            Quaternion frame = world.GetPose(owner).Rotation.ToQuaternion();
            MathF.Abs(Quaternion.Dot(hull, frame)).Should().BeGreaterThan(1 - 1e-5f, "the hull's rotation is the frame's, up to the sign of the quaternion");
            world.Errors.Should().BeEmpty();
        }
    }
}
