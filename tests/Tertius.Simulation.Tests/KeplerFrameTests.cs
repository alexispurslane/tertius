using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Kepler frames: a frame whose pose is a function of time. Every orbit here has a period of 4 s, and the world runs 64
/// ticks per second, which is a power of two, so the time step is exactly 1/64 s and a quarter period is exactly 64 ticks
/// with no rounding. The expected positions come from the geometry of the orbit, not from the orbit code: for a circle
/// of radius a, the position at mean anomaly M is (a cos M, 0, -a sin M), and the speed is a times the mean motion n.
/// The orbit runs counterclockwise seen from +Y, from the parent's +X toward its -Z. An orbit has no G·M of its own: the
/// root frame has a point gravity, and the Kepler frames in it take their G·M from that field.
/// </summary>
public class KeplerFrameTests
{
    private const double PeriodSeconds = 4;
    private const int TicksPerQuarterPeriod = 64;

    /// <summary>The mean motion of every orbit here, in radians per second.</summary>
    private static readonly double MeanMotion = 2 * Math.PI / PeriodSeconds;

    /// <summary>The parent's gravitational parameter that gives <paramref name="semiMajorAxis"/> the period <paramref name="periodSeconds"/> (Kepler's third law).</summary>
    private static double MuFor(double semiMajorAxis, double periodSeconds = PeriodSeconds)
    {
        double n = 2 * Math.PI / periodSeconds;
        return n * n * semiMajorAxis * semiMajorAxis * semiMajorAxis;
    }

    /// <summary>The semi-major axis of the orbits that go round the root, in meters.</summary>
    private const double RootOrbitRadius = 1000;

    /// <summary>The G·M of the point gravity of the root: the one that gives an orbit of <see cref="RootOrbitRadius"/> the period 4 s.</summary>
    private static readonly double RootMu = MuFor(RootOrbitRadius);

    private static KeplerOrbit Circle(double radius, double spinRate = 0) => KeplerOrbit.Circular(radius, spinRate: spinRate);

    private static KeplerOrbit Ellipse(double semiMajorAxis, double eccentricity) =>
        new(semiMajorAxis, eccentricity, 0, 0, 0, 0, 0, DQuaternion.Identity, 0, 0);

    private static TestWorld NewWorld(double startTime = 0)
    {
        var test = new TestWorld(configure: options =>
        {
            options.TickRate = 64;
            options.StartTime = startTime;
        });
        test.GiveRootPointGravity(RootMu);
        return test;
    }

    private static Entity SpawnKeplerFrame(TestWorld test, KeplerOrbit orbit, FrameHandle parent = default, GravityField gravity = default,
        params IComponent[] components) => SpawnKeplerFrame(test, orbit, firstMove: false, parent, gravity, components);

    private static Entity SpawnKeplerFrame(TestWorld test, KeplerOrbit orbit, bool firstMove, FrameHandle parent = default,
        GravityField gravity = default, params IComponent[] components) =>
        test.World.Spawn(new SpawnDesc
        {
            Frame = parent.IsNull ? test.Root : parent,
            AsFrame = new FrameSpec(FrameBounds.Sphere(1), gravity),
            Components = components,
        }.OnKeplerOrbit(test.World, orbit, firstMove));

    /// <summary>
    /// The frame has no body, so only its motion intents move it, and it spawns with no velocity. Its first motion intent
    /// carries it along the orbit from its first step, so its first sync point gives it the orbit's velocity at the end of
    /// that step, at mean anomaly n dt.
    /// </summary>
    [Fact]
    public void AKeplerFrameIsPlacedOnItsOrbitAtTheStartTimeAsSoonAsItExists()
    {
        using var test = NewWorld();
        FrameHandle moon = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Circle(1000), firstMove: true));

        // At mean anomaly 0 the body is at the parent's +X.
        PoseChecks.AssertNear(test.World.Frames.LocalPose(moon).Position, new Double3(1000, 0, 0), 1e-9);
        test.Run(1);
        // At mean anomaly M the body moves along (-sin M, 0, -cos M), so at M = 0 toward -Z, at the orbital speed.
        double m = MeanMotion * test.World.Time;
        PoseChecks.AssertNear(test.World.Frames.LinearVelocity(moon), 1000 * MeanMotion * new Double3(-Math.Sin(m), 0, -Math.Cos(m)), 1e-9);
    }

    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(1, 0, -1000)]
    [InlineData(2, -1000, 0)]
    [InlineData(3, 0, 1000)]
    [InlineData(4, 1000, 0)]
    public void ACircularKeplerFrameIsAtTheOrbitPointForTheTimeThatHasPassed(int quarterPeriods, double expectedX, double expectedZ)
    {
        using var test = NewWorld();
        FrameHandle moon = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Circle(1000)));

        test.Run(quarterPeriods * TicksPerQuarterPeriod);

        PoseChecks.AssertNear(test.World.Frames.LocalPose(moon).Position, new Double3(expectedX, 0, expectedZ), 1e-6);
    }

    [Fact]
    public void ACircularKeplerFramesVelocityIsTangentToItsOrbitAtTheOrbitalSpeed()
    {
        using var test = NewWorld();
        FrameHandle moon = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Circle(1000)));

        test.Run(TicksPerQuarterPeriod);

        // A quarter of the way round, at (0, 0, -1000), the body moves toward -X.
        PoseChecks.AssertNear(test.World.Frames.LinearVelocity(moon), new Double3(-1000 * MeanMotion, 0, 0), 1e-6);
    }

    /// <summary>
    /// The world starts one step before the orbit's epoch, so the comet's first step, which its first motion intent moves,
    /// ends at periapsis. The comet has no body, so it spawns with no velocity, and its first sync point gives it the
    /// velocity of that step's end.
    /// </summary>
    [Fact]
    public void AKeplerFrameOnAnEllipseStartsAtPeriapsisWhereItIsClosestAndFastest()
    {
        const double a = 1000, e = 0.5;
        using var test = NewWorld(startTime: -1.0 / 64);
        FrameHandle comet = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Ellipse(a, e), firstMove: true));

        test.Run(1);

        // Periapsis is at a (1 - e) on the +X axis, and the vis-viva speed there is a n sqrt((1 + e) / (1 - e)).
        PoseChecks.AssertNear(test.World.Frames.LocalPose(comet).Position, new Double3(a * (1 - e), 0, 0), 1e-9);
        PoseChecks.AssertNear(test.World.Frames.LinearVelocity(comet), new Double3(0, 0, -a * MeanMotion * Math.Sqrt((1 + e) / (1 - e))), 1e-6);
    }

    [Fact]
    public void AKeplerFrameOnAnEllipseIsAtApoapsisWhereItIsFarthestAndSlowestAfterHalfAPeriod()
    {
        const double a = 1000, e = 0.5;
        using var test = NewWorld();
        FrameHandle comet = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Ellipse(a, e)));

        test.Run(2 * TicksPerQuarterPeriod);

        // Apoapsis is at a (1 + e) on the -X axis, and the speed there is a n sqrt((1 - e) / (1 + e)), now toward +Z.
        PoseChecks.AssertNear(test.World.Frames.LocalPose(comet).Position, new Double3(-a * (1 + e), 0, 0), 1e-6);
        PoseChecks.AssertNear(test.World.Frames.LinearVelocity(comet), new Double3(0, 0, a * MeanMotion * Math.Sqrt((1 - e) / (1 + e))), 1e-6);
    }

    [Fact]
    public void AKeplerFrameSpinsAboutItsOwnUpAxisAtItsSpinRate()
    {
        using var test = NewWorld();
        // One turn per period: a quarter turn after a quarter period.
        FrameHandle planet = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Circle(1000, spinRate: MeanMotion)));

        test.Run(TicksPerQuarterPeriod);

        PoseChecks.AssertSameRotation(test.World.Frames.LocalPose(planet).Rotation, PoseChecks.QuarterTurnAboutY, 1e-9);
        PoseChecks.AssertNear(test.World.Frames.AngularVelocity(planet), new Vector3(0, (float)MeanMotion, 0), 1e-6f);
    }

    [Fact]
    public void AnEntityInAKeplerFrameKeepsItsLocalPoseAndMovesWithTheFrame()
    {
        using var test = NewWorld();
        FrameHandle moon = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Circle(1000)));
        Entity lander = test.World.SpawnAt(moon, new Double3(10, 0, 0));

        test.Run(TicksPerQuarterPeriod);

        test.World.PoseInOwnFrame(lander).Position.Should().Be(new Double3(10, 0, 0));
        test.World.Snapshot.TryGetPoseIn(lander, test.Root, out DPose inRoot).Should().BeTrue();
        // The frame has no spin, so the lander's 10 m along +X stays along +X: (0, 0, -1000) + (10, 0, 0).
        PoseChecks.AssertNear(inRoot.Position, new Double3(10, 0, -1000), 1e-6);
    }

    [Fact]
    public void AnEntityInASpinningKeplerFrameTurnsWithTheFrame()
    {
        using var test = NewWorld();
        FrameHandle planet = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Circle(1000, spinRate: MeanMotion)));
        Entity lander = test.World.SpawnAt(planet, new Double3(10, 0, 0));

        test.Run(TicksPerQuarterPeriod);

        // The quarter turn maps +X to -Z: the lander's 10 m along +X are now 10 m along -Z, from (0, 0, -1000).
        test.World.Snapshot.TryGetPoseIn(lander, test.Root, out DPose inRoot).Should().BeTrue();
        PoseChecks.AssertNear(inRoot.Position, new Double3(0, 0, -1010), 1e-6);
    }

    [Fact]
    public void TheOwnerOfAKeplerFrameMirrorsTheFramesStateAtTheEndOfEveryTick()
    {
        using var test = NewWorld();
        Entity owner = SpawnKeplerFrame(test, Circle(1000, spinRate: 0.5));
        FrameHandle frame = test.World.GetOwnedFrame(owner);

        for (int tick = 0; tick < 5; tick++)
        {
            test.Run(1);

            test.World.PoseInOwnFrame(owner).Should().Be(test.World.Frames.LocalPose(frame));
            test.World.VelocityInOwnFrame(owner).Should().Be(test.World.Frames.LinearVelocity(frame).ToVector3());
            test.World.AngularVelocityInOwnFrame(owner).Should().Be(test.World.Frames.AngularVelocity(frame));
            test.World.GetFrame(owner).Should().Be(test.Root);
        }
    }

    [Fact]
    public void AKeplerFrameHasNoProperAccelerationBecauseItIsInFreeFall()
    {
        using var test = NewWorld();
        FrameHandle moon = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Circle(1000)));

        test.Run(10);

        test.World.Frames.ProperAcceleration(moon).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void AMoonRidesItsPlanetAlongTheMoonsOwnOrbit()
    {
        using var test = NewWorld();
        // The moon's period is 2 s, so it is half a turn round the planet after one second, when the planet is a quarter turn round the root.
        // The planet's own point gravity has the G·M that gives a circle of 10 m the period 2 s.
        FrameHandle planet = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Circle(1000), gravity: GravityField.Point(MuFor(10, periodSeconds: 2))));
        var moonOrbit = KeplerOrbit.Circular(10);
        FrameHandle moon = test.World.GetOwnedFrame(SpawnKeplerFrame(test, moonOrbit, planet));
        Entity base_ = test.World.SpawnAt(moon, Double3.Zero);

        test.Run(TicksPerQuarterPeriod);

        PoseChecks.AssertNear(test.World.Frames.LocalPose(moon).Position, new Double3(-10, 0, 0), 1e-6);
        test.World.Snapshot.TryGetPoseIn(base_, test.Root, out DPose inRoot).Should().BeTrue();
        PoseChecks.AssertNear(inRoot.Position, new Double3(-10, 0, -1000), 1e-6);
    }

    [Fact]
    public void AWorldThatStartsLaterPlacesAKeplerFrameWhereItsOrbitIsAtThatTime()
    {
        // 5001 s is 1250 periods and a quarter, so the body is a quarter of the way round.
        using var test = NewWorld(startTime: 5001);
        FrameHandle moon = test.World.GetOwnedFrame(SpawnKeplerFrame(test, Circle(1000)));

        test.World.Time.Should().Be(5001);
        PoseChecks.AssertNear(test.World.Frames.LocalPose(moon).Position, new Double3(0, 0, -1000), 1e-6);

        test.Run(TicksPerQuarterPeriod);

        test.World.Time.Should().Be(5002);
        PoseChecks.AssertNear(test.World.Frames.LocalPose(moon).Position, new Double3(-1000, 0, 0), 1e-6);
    }

    [Fact]
    public void WhenItsOwnerWritesItsTransformABodylessKeplerFrameMovesForOneTickAndItsNextSamplePutsItBackOnItsOrbit()
    {
        using var test = NewWorld();
        bool write = false;
        Entity owner = SpawnKeplerFrame(test, Circle(1000),
            components: Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
            {
                if (write)
                    self.SetTransform(TestWorld.At(5));
                write = false;
            }));
        FrameHandle moon = test.World.GetOwnedFrame(owner);
        double mu = KeplerOrbit.MuFrom(test.World.Frames.Gravity(test.Root));
        test.Run(3);

        write = true;
        test.Run(1);

        test.World.Errors.Should().BeEmpty();
        test.World.Frames.LocalPose(moon).Position.Should().Be(new Double3(5, 0, 0), "the write moves the frame at the sync point of its tick");
        test.Run(1);

        test.World.Frames.LocalPose(moon).Position.Should().Be(Circle(1000).StateAt(test.World.Time, mu).Position, "the next sample puts the frame back on its orbit");
    }
}
