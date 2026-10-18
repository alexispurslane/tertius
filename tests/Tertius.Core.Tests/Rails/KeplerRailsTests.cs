using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The solution of Kepler's equation behind <see cref="KeplerRails.StateAt"/> and the attitudes: the frame moves without a
/// jump at every eccentricity that the orbit allows, and its clock starts at its epoch. Also the chord and the acceleration
/// of an orbit, and the G·M that an orbit takes from the gravity of its parent (<see cref="KeplerRails.OrbitMu"/>).
/// </summary>
public class KeplerRailsTests
{
    private const double Mu = 3.986004418e14;
    private const double Tick = 1.0 / 60;

    private static KeplerOrbit Oval(double eccentricity, KeplerAttitude attitude = KeplerAttitude.Spin) =>
        new(1e7, eccentricity, 0, 0, 0, 0, 0, DQuaternion.Identity, 0, 0, attitude);

    [Theory]
    [InlineData(0.8)]
    [InlineData(0.9)]
    [InlineData(0.99)]
    public void OnAnOrbitOfHighEccentricityTheFrameMovesNoFurtherInATickThanItsSpeedCarriesIt(double eccentricity)
    {
        KeplerOrbit orbit = Oval(eccentricity);
        (Double3 position, Double3 velocity) = KeplerRails.StateAt(orbit, 0, Mu);

        for (long tick = 1; tick * Tick <= KeplerRails.Period(orbit, Mu); tick++)
        {
            (Double3 next, Double3 nextVelocity) = KeplerRails.StateAt(orbit, tick * Tick, Mu);
            // Along one tick, the speed changes by under 1% even at the periapsis of e = 0.99.
            double reach = Math.Max(velocity.Length, nextVelocity.Length) * Tick * 1.05;
            Double3.Distance(next, position).Should().BeLessThan(reach, $"the frame jumps at tick {tick}");
            (position, velocity) = (next, nextVelocity);
        }
    }

    [Theory]
    [InlineData(0.8)]
    [InlineData(0.9)]
    [InlineData(0.99)]
    public void OnAnOrbitOfHighEccentricityAFacingFrameTurnsNoFurtherInATickThanItsAngularVelocityCarriesIt(double eccentricity)
    {
        KeplerOrbit orbit = Oval(eccentricity, KeplerAttitude.FacesParent);
        DQuaternion rotation = KeplerRails.RotationAt(orbit, 0, Mu);
        double rate = KeplerRails.AngularVelocityAt(orbit, 0, Mu).Length;

        for (long tick = 1; tick * Tick <= KeplerRails.Period(orbit, Mu); tick++)
        {
            DQuaternion next = KeplerRails.RotationAt(orbit, tick * Tick, Mu);
            double nextRate = KeplerRails.AngularVelocityAt(orbit, tick * Tick, Mu).Length;
            double dot = Math.Abs(rotation.X * next.X + rotation.Y * next.Y + rotation.Z * next.Z + rotation.W * next.W);
            double angle = 2 * Math.Acos(Math.Min(dot, 1));
            // The small absolute margin covers the rounding of Acos near 1.
            angle.Should().BeLessThan(Math.Max(rate, nextRate) * Tick * 1.05 + 1e-6, $"the frame flips at tick {tick}");
            (rotation, rate) = (next, nextRate);
        }
    }

    [Fact]
    public void ASpinningOrbitStandsAtItsSpinPhaseAtItsEpochAndTurnsAtItsRate()
    {
        KeplerOrbit orbit = new(1e7, 0, 0, 0, 0, 0, 40, DQuaternion.Identity, 3, Math.PI / 2);

        // At the epoch, a quarter turn about +Y takes +X to -Z; π/6 s later, 3 rad/s has turned it another half turn.
        Double3.Distance(KeplerRails.RotationAt(orbit, 40, Mu).Rotate(Double3.UnitX), new Double3(0, 0, -1)).Should().BeLessThan(1e-12);
        Double3.Distance(KeplerRails.RotationAt(orbit, 40 + Math.PI / 6, Mu).Rotate(Double3.UnitX), new Double3(-1, 0, 0)).Should().BeLessThan(1e-12);
    }

    [Fact]
    public void AnOrbitWhoseMeanAnomalyIsZeroAtItsEpochIsAtPeriapsisThen()
    {
        // Periapsis lies along +X, at a (1 - e) = 7,000 km, and the epoch is 100 s.
        KeplerOrbit orbit = new(1e7, 0.3, 0, 0, 0, 0, 100, DQuaternion.Identity, 0, 0, KeplerAttitude.FacesParent);

        Double3.Distance(KeplerRails.StateAt(orbit, 100, Mu).Position, new Double3(7e6, 0, 0)).Should().BeLessThan(1e-3);
        Double3.Distance(KeplerRails.RotationAt(orbit, 100, Mu).Rotate(new Double3(0, -1, 0)), new Double3(-1, 0, 0)).Should().BeLessThan(1e-9);
    }

    /// <summary>
    /// A tight circular orbit, 100 m round a body with a G·M of 1e6 m³/s², so that it turns at 1 rad/s: its acceleration
    /// of 100 m/s² and its jerk of 100 m/s³ show in the chord of one step.
    /// </summary>
    private static readonly KeplerOrbit Tight = KeplerOrbit.Circular(100, phase: 0.3);
    private const double TightMu = 1e6;
    private const double Step = 1.0 / 60;

    [Fact]
    public void The_chord_of_an_orbit_is_its_change_of_place_over_the_step_that_ends_now_divided_by_the_step()
    {
        (Double3 before, _) = KeplerRails.StateAt(Tight, 10 - Step, TightMu);
        (Double3 now, Double3 velocity) = KeplerRails.StateAt(Tight, 10, TightMu);
        Double3 chord = KeplerRails.Chord(now, velocity, TightMu, Step);
        (chord - (now - before) / Step).Length.Should().BeLessThan(3e-5,
            "the series stops at the jerk, so it leaves out the snap of 100 m/s⁴ times the cube of the step over 24, 2e-5 m/s");
        (chord - velocity).Length.Should().BeGreaterThan(0.8, "the chord lags the instant velocity by half a step of the 100 m/s² acceleration");
    }

    [Fact]
    public void A_frame_that_starts_a_step_at_the_chord_with_the_orbits_acceleration_ends_it_on_the_orbit()
    {
        // The scheme that Jolt uses for bodies, and the numeric update for falling frames: the velocity first, then the place.
        (Double3 now, Double3 velocity) = KeplerRails.StateAt(Tight, 10, TightMu);
        (Double3 next, _) = KeplerRails.StateAt(Tight, 10 + Step, TightMu);
        Double3 chord = KeplerRails.Chord(now, velocity, TightMu, Step);
        Double3 after = now + (chord + KeplerRails.Acceleration(now, TightMu) * Step) * Step;
        (after - next).Length.Should().BeLessThan(1e-6);
    }

    [Fact]
    public void The_acceleration_of_an_orbit_is_the_pull_of_its_point_gravity()
    {
        KeplerRails.Acceleration(new Double3(0, 0, 100), TightMu).Should().Be(new Double3(0, 0, -100));
        KeplerRails.Acceleration(new Double3(200, 0, 0), TightMu).Should().Be(new Double3(-25, 0, 0));
    }

    // ---- The G·M of an orbit -------------------------------------------------------------------------------------

    [Fact]
    public void AnOrbitTakesItsGMFromAPointFieldThatCanBeOrbited()
    {
        KeplerRails.OrbitMu(GravityField.Point(Mu)).Should().Be(Mu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnOrbitTakesNoGMFromAPointFieldThatCannotBeOrbited(double mu)
    {
        KeplerRails.OrbitMu(GravityField.Point(mu)).Should().Be(0);
    }

    [Fact]
    public void AnOrbitTakesNoGMFromAUniformField()
    {
        var down = new Vector3(0, -9.81f, 0);

        KeplerRails.OrbitMu(GravityField.Uniform(down)).Should().Be(0);
        KeplerRails.OrbitMu(GravityField.Uniform(down, new BoundingBox(new Vector3(-10), new Vector3(10)))).Should().Be(0);
    }

    [Fact]
    public void AnOrbitTakesNoGMFromNoGravity()
    {
        KeplerRails.OrbitMu(GravityField.None).Should().Be(0);
    }
}
