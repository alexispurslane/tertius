using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A Kepler frame takes its G·M from the point gravity of its parent frame (ter-7wbc), and physics pulls the loose bodies
/// in that parent with the same field. So a loose body in the parent that starts with the velocity of a Kepler frame, close
/// to the frame, keeps pace with it, on the World and the real Jolt backend. Before, the orbit held a G·M of its own, and
/// the two could differ: a body that left a ship then fell on another orbit than the ship's.
/// <para>
/// The planet has the G·M of Earth, and the frame goes round it on a circle of 7,000 km, at about 7,546 m/s. How close the
/// body keeps to the frame is not a matter of luck: <see cref="Tolerance"/> derives it. The derivation also gives the
/// direction of the biggest error, so the tests check the direction as well as the size. A G·M that is off by one part in a
/// thousand is larger than the bound.
/// </para>
/// </summary>
public sealed class KeplerFrameWithLooseBodyTests
{
    private const double PlanetMu = 3.986004418e14;
    private const double OrbitRadius = 7e6;

    /// <summary>The fixed time step of the world, in seconds: 1 / <see cref="WorldOptions.TickRate"/> at its default of 60.</summary>
    private const double Dt = 1.0 / 60;

    /// <summary>How long a body flies in the parent frame in these tests, in seconds. A whole orbit takes 5,828 s.</summary>
    private const double Seconds = 60;

    /// <summary>The pull of the planet at the orbit, in m/s².</summary>
    private static double Pull => PlanetMu / (OrbitRadius * OrbitRadius);

    /// <summary>The angular speed of the orbit, in rad/s: √(G·M / r³).</summary>
    private static double MeanMotion => Math.Sqrt(PlanetMu / (OrbitRadius * OrbitRadius * OrbitRadius));

    /// <summary>
    /// How far a body can be off its place after <paramref name="seconds"/> in the parent frame, when it starts in the right
    /// place with the right velocity and is at most <paramref name="distance"/> meters from the Kepler frame. The frame is
    /// exactly on its orbit, so the whole error is what Jolt's step and the engine's model of the frame do not share. All
    /// values are in meters. <c>Inward</c> is how far the body can lie nearer to the planet than the orbit puts it, and
    /// <c>Outward</c> how far it can lie farther away. <c>Across</c> is how far it can lie off the line to the planet.
    /// <list type="number">
    /// <item>
    /// The step. Jolt adds the pull to the velocity first, then moves the body by the new velocity. So a body that starts
    /// with the exact velocity moves as if its velocity were too large by half a step of the pull: <c>g · dt / 2</c>,
    /// which is 0.068 m/s. That error points along the pull, toward the planet, so the body lies <b>inward</b>. The error
    /// in the velocity adds up to <c>g · dt / 2 · seconds</c> in the radial position. It bends into the along-track
    /// direction by <c>g · dt / 2 · n · seconds²</c>, where n is the angular speed of the orbit.
    /// </item>
    /// <item>
    /// The tide. The frame and the body are at different places, so the planet pulls them a little differently. The
    /// difference of the pulls is at most <c>2 n² · distance</c>. That adds up to <c>n² · distance · seconds²</c>, in
    /// either direction.
    /// </item>
    /// <item>
    /// The float rounding. Jolt keeps velocities in 32-bit floats, and the speed of 7,546 m/s has a spacing of 4.9e-4 m/s
    /// between floats. Each step rounds the velocity by at most half of that, in a pattern that is not related to the
    /// orbit, so the errors add up like a random walk. Over <c>N = seconds / dt</c> steps, the position drifts by a standard
    /// deviation of <c>seconds · spacing · √N / 6</c>, in either direction. The bound takes 5 of them.
    /// </item>
    /// </list>
    /// </summary>
    private static (double Inward, double Outward, double Across) Tolerance(double distance, double seconds)
    {
        double speed = Math.Sqrt(PlanetMu / OrbitRadius);
        double floatSpacing = MathF.BitIncrement((float)speed) - (float)speed;
        double stepVelocity = Pull * Dt / 2;
        double radialStep = stepVelocity * seconds;
        double bend = stepVelocity * MeanMotion * seconds * seconds;
        double tide = MeanMotion * MeanMotion * distance * seconds * seconds;
        double rounding = 5 * seconds * floatSpacing * Math.Sqrt(seconds / Dt) / 6;
        return (radialStep + tide + rounding, tide + rounding, bend + tide + rounding);
    }

    /// <summary>
    /// Checks the <paramref name="error"/> of a body, its position minus the place that the orbit gives it, against
    /// <see cref="Tolerance"/>. <paramref name="away"/> is the unit vector from the planet to that place.
    /// </summary>
    private static void AssertKeepsPace(Double3 error, Double3 away, double distance)
    {
        (double inward, double outward, double across) = Tolerance(distance, Seconds);
        double radial = Double3.Dot(error, away);
        double sideways = (error - away * radial).Length;
        radial.Should().BeInRange(-inward, outward,
            "the body falls on the G·M of the planet's field, which is also the G·M of the frame's orbit. Jolt's step puts it nearer to the planet by up to {0} m",
            inward);
        sideways.Should()
            .BeLessThan(across, "the body does not drift along the track or out of the plane of the orbit");
    }

    /// <summary>A planet with the G·M of Earth, and a frame on a circular orbit around it whose bubble is 50 m in radius.</summary>
    private static (World World, FrameHandle Planet, FrameHandle Ship) PlanetWithShip()
    {
        World world = NewWorld(backend: out _);
        FrameHandle planet = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(PlanetMu)));
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            AsFrame = new(FrameBounds.Sphere(50))
        }.OnKeplerOrbit(world, KeplerOrbit.Circular(OrbitRadius, phase: 0.7))));
        Tick(world, 5);
        return (world, planet, ship);
    }

    /// <summary>The angle of the frame round the planet's +Y, counterclockwise seen from +Y, in radians: from +X toward -Z.</summary>
    private static double AngleAround(Double3 position) => Math.Atan2(-position.Z, position.X);

    /// <summary>
    /// A ball in the frame drifts out at 2 m/s relative to the frame. Physics hands it to the planet's frame, with the
    /// velocity that the frame had there. The ball is in the planet's frame for another minute. In the frame's axes, which
    /// are the planet's, it keeps drifting at 2 m/s. It is where it would be in a frame that falls with the ship.
    /// </summary>
    [Fact]
    public void ABodyThatLeavesAKeplerFrameKeepsDriftingAwayFromItAtItsOwnSpeedForAMinute()
    {
        (World world, FrameHandle planet, FrameHandle ship) = PlanetWithShip();
        using (world)
        {
            double released = world.Time;
            Entity ball = world.Spawn(new()
            {
                Frame = ship,
                Pose = At(30),
                LinearVelocity = new Vector3(2, 0, 0),
                Body = new(Shape: new SphereShapeDesc(0.5f), Mass: 1, LinearDamping: 0)
            });
            TickUntil(world, () => world.GetFrame(ball) == planet, 2000);
            world.GetFrame(ball).Should().Be(planet, "the ball left the bubble of the frame");
            double leftAt = world.Time;

            Tick(world, (int)(Seconds / Dt));

            Double3 shipNow = world.Frames.LocalPose(ship).Position;
            Double3 apart = world.PoseInOwnFrame(ball).Position - shipNow;
            Double3 expected = new(30 + 2 * (world.Time - released), 0, 0);
            (world.Time - leftAt).Should().BeApproximately(Seconds, 1e-3, "the world adds up its time steps");
            AssertKeepsPace(apart - expected, shipNow.Normalized(), apart.Length);
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A ball is released in the planet's frame 200 m ahead of the frame, outside its bubble, on the same circle round the
    /// planet: it has the state of the frame, turned about the planet's +Y by 200 m of arc. It goes round the planet next to
    /// the frame. Turned back by the angle that the frame has gone round, its offset from the frame is the same as at the start.
    /// </summary>
    [Fact]
    public void ALooseBodyReleasedAheadOfAKeplerFrameWithItsVelocityStaysAheadOfItForAMinute()
    {
        (World world, FrameHandle planet, FrameHandle ship) = PlanetWithShip();
        using (world)
        {
            Double3 shipStart = world.Frames.LocalPose(ship).Position;
            DQuaternion arc = DQuaternion.FromAxisAngle(Double3.UnitY, 200 / OrbitRadius);
            Double3 ballStart = arc.Rotate(shipStart);
            Double3 ahead = ballStart - shipStart;
            Entity ball = world.Spawn(new()
            {
                Frame = planet,
                Pose = new(ballStart, DQuaternion.Identity),
                LinearVelocity = arc.Rotate(world.Frames.LinearVelocity(ship)).ToVector3(),
                Body = new(Shape: new SphereShapeDesc(0.5f), Mass: 1, LinearDamping: 0)
            });

            Tick(world, (int)(Seconds / Dt));

            world.GetFrame(ball).Should().Be(planet, "200 m is outside the 50 m bubble of the frame");
            Double3 shipNow = world.Frames.LocalPose(ship).Position;
            double turned = AngleAround(shipNow) - AngleAround(shipStart);
            Double3 offset = DQuaternion.FromAxisAngle(Double3.UnitY, -turned)
                .Rotate(world.PoseInOwnFrame(ball).Position - shipNow);
            AssertKeepsPace(offset - ahead, shipStart.Normalized(), 200);
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The bound has to be tight enough to mean something. If the field of the planet and the G·M of the orbit differed by one
    /// part in a thousand, the pull would differ by <c>g / 1000</c>, and that moves the body by <c>g / 1000 · seconds² / 2</c>
    /// .
    /// </summary>
    [Fact]
    public void TheBoundIsSmallerThanTheEffectOfAnErrorOfOnePartInAThousandInTheGM()
    {
        double effectOfOnePerMille = Pull / 1000 * Seconds * Seconds / 2;
        (double inward, double outward, _) = Tolerance(200, Seconds);

        (inward + outward).Should().BeLessThan(effectOfOnePerMille);
    }
}
