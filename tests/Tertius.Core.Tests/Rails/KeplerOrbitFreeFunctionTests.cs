using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The static members of <see cref="KeplerOrbit"/>, which take a state and a G·M instead of an orbit: the chord and the
/// acceleration of an orbit, and the G·M that an orbit takes from the gravity of its parent (<see cref="KeplerOrbit.MuFrom"/>).
/// </summary>
public class KeplerOrbitFreeFunctionTests
{
    private const double Mu = 3.986004418e14;

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
        (Double3 before, _) = Tight.StateAt(10 - Step, TightMu);
        (Double3 now, Double3 velocity) = Tight.StateAt(10, TightMu);
        Double3 chord = KeplerOrbit.Chord(now, velocity, TightMu, Step);
        (chord - (now - before) / Step).Length.Should().BeLessThan(3e-5,
            "the series stops at the jerk, so it leaves out the snap of 100 m/s⁴ times the cube of the step over 24, 2e-5 m/s");
        (chord - velocity).Length.Should().BeGreaterThan(0.8, "the chord lags the instant velocity by half a step of the 100 m/s² acceleration");
    }

    [Fact]
    public void A_frame_that_starts_a_step_at_the_chord_with_the_orbits_acceleration_ends_it_on_the_orbit()
    {
        // The scheme that Jolt uses for bodies, and the numeric update for falling frames: the velocity first, then the place.
        (Double3 now, Double3 velocity) = Tight.StateAt(10, TightMu);
        (Double3 next, _) = Tight.StateAt(10 + Step, TightMu);
        Double3 chord = KeplerOrbit.Chord(now, velocity, TightMu, Step);
        Double3 after = now + (chord + KeplerOrbit.Acceleration(now, TightMu) * Step) * Step;
        (after - next).Length.Should().BeLessThan(1e-6);
    }

    [Fact]
    public void The_acceleration_of_an_orbit_is_the_pull_of_its_point_gravity()
    {
        KeplerOrbit.Acceleration(new Double3(0, 0, 100), TightMu).Should().Be(new Double3(0, 0, -100));
        KeplerOrbit.Acceleration(new Double3(200, 0, 0), TightMu).Should().Be(new Double3(-25, 0, 0));
    }

    // ---- The G·M of an orbit -------------------------------------------------------------------------------------

    [Fact]
    public void AnOrbitTakesItsGMFromAPointFieldThatCanBeOrbited()
    {
        KeplerOrbit.MuFrom(GravityField.Point(Mu)).Should().Be(Mu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnOrbitTakesNoGMFromAPointFieldThatCannotBeOrbited(double mu)
    {
        KeplerOrbit.MuFrom(GravityField.Point(mu)).Should().Be(0);
    }

    [Fact]
    public void AnOrbitTakesNoGMFromAUniformField()
    {
        var down = new Vector3(0, -9.81f, 0);

        KeplerOrbit.MuFrom(GravityField.Uniform(down)).Should().Be(0);
        KeplerOrbit.MuFrom(GravityField.Uniform(down, new BoundingBox(new Vector3(-10), new Vector3(10)))).Should().Be(0);
    }

    [Fact]
    public void AnOrbitTakesNoGMFromNoGravity()
    {
        KeplerOrbit.MuFrom(GravityField.None).Should().Be(0);
    }
}
