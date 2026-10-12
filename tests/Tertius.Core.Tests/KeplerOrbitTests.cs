using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The solution of Kepler's equation behind <see cref="KeplerOrbit.StateAt"/> and the attitudes: the frame moves without a
/// jump at every eccentricity that the orbit allows, and its clock starts at its epoch.
/// </summary>
public class KeplerOrbitTests
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
        (Double3 position, Double3 velocity) = orbit.StateAt(0, Mu);

        for (long tick = 1; tick * Tick <= orbit.Period(Mu); tick++)
        {
            (Double3 next, Double3 nextVelocity) = orbit.StateAt(tick * Tick, Mu);
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
        DQuaternion rotation = orbit.RotationAt(0, Mu);
        double rate = orbit.AngularVelocityAt(0, Mu).Length;

        for (long tick = 1; tick * Tick <= orbit.Period(Mu); tick++)
        {
            DQuaternion next = orbit.RotationAt(tick * Tick, Mu);
            double nextRate = orbit.AngularVelocityAt(tick * Tick, Mu).Length;
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
        Double3.Distance(orbit.RotationAt(40, Mu).Rotate(Double3.UnitX), new Double3(0, 0, -1)).Should().BeLessThan(1e-12);
        Double3.Distance(orbit.RotationAt(40 + Math.PI / 6, Mu).Rotate(Double3.UnitX), new Double3(-1, 0, 0)).Should().BeLessThan(1e-12);
    }

    [Fact]
    public void AnOrbitWhoseMeanAnomalyIsZeroAtItsEpochIsAtPeriapsisThen()
    {
        // Periapsis lies along +X, at a (1 - e) = 7,000 km, and the epoch is 100 s.
        KeplerOrbit orbit = new(1e7, 0.3, 0, 0, 0, 0, 100, DQuaternion.Identity, 0, 0, KeplerAttitude.FacesParent);

        Double3.Distance(orbit.StateAt(100, Mu).Position, new Double3(7e6, 0, 0)).Should().BeLessThan(1e-3);
        Double3.Distance(orbit.RotationAt(100, Mu).Rotate(new Double3(0, -1, 0)), new Double3(-1, 0, 0)).Should().BeLessThan(1e-9);
    }
}
