using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The attitude of a Kepler frame that faces its parent: its belly (-Y) toward the parent and its nose (-Z) along its
/// motion across the horizon, like a station in low orbit. The orbits here go round Earth.
/// </summary>
public class KeplerAttitudeTests
{
    private const double Mu = 3.986004418e14;
    private const double LowOrbit = 6.771e6;

    private static KeplerOrbit Facing(KeplerOrbit orbit) => orbit with { Attitude = KeplerAttitude.FacesParent };

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance = 1e-9) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Theory]
    [InlineData(0)]
    [InlineData(0.13)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void OnARoundOrbitTheNosePointsAlongTheMotionAndTheBellyAtTheParent(double fractionOfPeriod)
    {
        KeplerOrbit orbit = Facing(KeplerOrbit.Circular(LowOrbit, Mu));
        double time = fractionOfPeriod * orbit.Period;
        // The orbit's convention: at mean anomaly M the frame is at a (cos M, 0, -sin M) and moves along (-sin M, 0, -cos M).
        double m = 2 * Math.PI * fractionOfPeriod;

        DQuaternion rotation = orbit.RotationAt(time);

        ExpectNear(rotation.Rotate(new Double3(0, 0, -1)), new Double3(-Math.Sin(m), 0, -Math.Cos(m)));
        ExpectNear(rotation.Rotate(new Double3(0, -1, 0)), new Double3(-Math.Cos(m), 0, Math.Sin(m)));
    }

    [Fact]
    public void OnARoundOrbitTheLeftRightAxisLiesAlongTheOrbitsNormal()
    {
        KeplerOrbit orbit = Facing(KeplerOrbit.Circular(LowOrbit, Mu));

        // The orbit runs counterclockwise seen from +Y. With the nose along the motion and the belly toward the parent, the
        // frame's +X points to -Y.
        ExpectNear(orbit.RotationAt(1234).Rotate(Double3.UnitX), new Double3(0, -1, 0));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.37)]
    [InlineData(0.8)]
    public void OnAnOvalOrbitTheBellyPointsExactlyAtTheParentAndTheNoseAcrossTheHorizonInTheDirectionOfMotion(double fractionOfPeriod)
    {
        KeplerOrbit orbit = Facing(new KeplerOrbit(1e7, 0.3, 0.4, 1.0, 0.5, 0, 0, Mu, DQuaternion.Identity, 0, 0));
        double time = fractionOfPeriod * orbit.Period;
        (Double3 position, Double3 velocity) = orbit.StateAt(time);

        DQuaternion rotation = orbit.RotationAt(time);

        Double3 belly = rotation.Rotate(new Double3(0, -1, 0));
        Double3 nose = rotation.Rotate(new Double3(0, 0, -1));
        ExpectNear(belly, -position.Normalized());
        Double3.Dot(nose, position).Should().BeApproximately(0, 1e-3, "the nose lies along the horizon");
        Double3.Dot(nose, velocity).Should().BePositive("the nose points the way that the frame moves");
    }

    [Fact]
    public void OnARoundOrbitTheAngularVelocityIsTheMeanMotionAboutTheOrbitsNormal()
    {
        KeplerOrbit orbit = Facing(KeplerOrbit.Circular(LowOrbit, Mu));
        double n = Math.Sqrt(Mu / (LowOrbit * LowOrbit * LowOrbit));

        Double3 omega = orbit.AngularVelocityAt(500);

        ExpectNear(omega, new Double3(0, n, 0), 1e-12);
    }

    [Fact]
    public void OnAnOvalOrbitTheFrameTurnsFasterAtPeriapsisThanAtApoapsisByTheSquareOfTheirDistances()
    {
        const double e = 0.3;
        KeplerOrbit orbit = Facing(new KeplerOrbit(1e7, e, 0, 0, 0, 0, 0, Mu, DQuaternion.Identity, 0, 0));

        double atPeriapsis = orbit.AngularVelocityAt(0).Length;
        double atApoapsis = orbit.AngularVelocityAt(orbit.Period / 2).Length;

        // The turn rate is h / r², and the distances at the apsides are a (1 - e) and a (1 + e).
        (atPeriapsis / atApoapsis).Should().BeApproximately(Math.Pow((1 + e) / (1 - e), 2), 1e-9);
    }

    [Fact]
    public void TheRotationTurnsAtItsAngularVelocity()
    {
        KeplerOrbit orbit = Facing(new KeplerOrbit(1e7, 0.3, 0.4, 1.0, 0.5, 0, 0, Mu, DQuaternion.Identity, 0, 0));
        const double time = 1000, step = 0.01;

        // The change of the rotation over a short step, as an angle about an axis, is the angular velocity times the step.
        DQuaternion change = orbit.RotationAt(time + step) * orbit.RotationAt(time).Conjugate();
        Double3 axisTimesSine = new Double3(change.X, change.Y, change.Z) * Math.Sign(change.W);
        Double3 omega = orbit.AngularVelocityAt(time + step / 2);

        ExpectNear(axisTimesSine * (2 / step), omega, 1e-8);
    }

    [Fact]
    public void ASpinningFrameKeepsItsSpinWhateverItsPlaceOnTheOrbit()
    {
        KeplerOrbit orbit = KeplerOrbit.Circular(LowOrbit, Mu, spinRate: 0.01);

        orbit.AngularVelocityAt(0).Should().Be(orbit.AngularVelocityAt(1000));
        orbit.AngularVelocityAt(0).Should().Be(new Double3(0, 0.01, 0));
    }

    [Fact]
    public void AnOrbitWithAnAttitudeThatIsNotDefinedIsNotValid()
    {
        (KeplerOrbit.Circular(LowOrbit, Mu) with { Attitude = (KeplerAttitude)7 }).IsValid.Should().BeFalse();
    }
}
