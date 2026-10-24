using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// The predictions of a throw in the spinning wheel (docs/plans/blue-danube-demo.md, "The coda"): the straight line of the
/// release in inertial space, seen in the station's turning axes, and the parabola of the same release over a flat floor
/// in a field of the floor's gravity. The expected values come from the same line worked out by hand in python, on a
/// floor of so many facets that it is the circle of 100 m. Pure, with no World.
/// </summary>
public sealed class ThrowReleaseTests
{
    private const double Rate = 0.31321;

    /// <summary>A floor of 2^20 facets: within 0.5 µm of the circle of 100 m.</summary>
    private static readonly WheelFloor Circle = new(100, 1 << 20);

    /// <summary>
    /// The eye of a person who stands at θ = 180 degrees, 1.65 m above the floor: over the middle of a facet, so that the
    /// flat field's floor, the facet's plane, is the circle's tangent there. The circle has no facets to speak of, so the
    /// numbers are those of any place round it.
    /// </summary>
    private static readonly Double3 Eye = Circle.OnFacet(facet: Circle.Segments / 2, height: 1.65, y: 0);

    /// <summary>8 m/s at 30 degrees above the floor at the eye, spinward for 1 and antispinward for -1.</summary>
    private static Double3 Throw(int way) => WheelFloor.UpAt(Eye) * (8 * Math.Sin(Math.PI / 6)) +
                                             WheelFloor.SpinwardAt(Eye) * (way * 8 * Math.Cos(Math.PI / 6));

    [Fact]
    public void ABoxDroppedFromRestLandsAntispinwardOfThePointUnderIt()
    {
        var release = new ThrowRelease(new(-99, 0, 0), Double3.Zero);

        ThrowLanding landing = release.Landing(Rate, Circle, 0);

        landing.Time.Should().BeApproximately(0.4549417, 1e-6);
        Circle.Spinward(release.Place, landing.Place).Should().BeApproximately(-0.0952809, 1e-6);
    }

    [Fact]
    public void AThrowSpinwardLandsShortOfTheFlatFieldsPoint()
    {
        var release = new ThrowRelease(Eye, Throw(1));

        ThrowLanding landing = release.Landing(Rate, Circle, 0);

        landing.Time.Should().BeApproximately(0.8227725, 1e-6);
        Circle.Spinward(release.Place, landing.Place).Should().BeApproximately(5.7967776, 1e-6);
        release.FlatLanding(Rate * Rate * Circle.Radius, Circle, 0).Spinward.Should().BeApproximately(7.7368649, 1e-6);
    }

    [Fact]
    public void AThrowAntispinwardLandsLongOfTheFlatFieldsPoint()
    {
        var release = new ThrowRelease(Eye, Throw(-1));

        ThrowLanding landing = release.Landing(Rate, Circle, 0);
        (double time, double spinward) = release.FlatLanding(Rate * Rate * Circle.Radius, Circle, 0);

        landing.Time.Should().BeApproximately(1.6757591, 1e-6);
        Circle.Spinward(release.Place, landing.Place).Should().BeApproximately(-11.3233915, 1e-6);
        time.Should().BeApproximately(1.1167203, 1e-6);
        spinward.Should().BeApproximately(-7.7368649, 1e-6);
    }

    [Fact]
    public void ABoxLandsWhenItsCenterComesDownToItsHalfSizeAboveTheFacet()
    {
        var release = new ThrowRelease(new(-99, 0, 0), Double3.Zero);
        var floor = new WheelFloor(100, 256);

        ThrowLanding landing = release.Landing(Rate, floor, 0.25);

        floor.Height(landing.Place).Should().BeApproximately(0.25, 1e-9);
        floor.Height(release.PlaceAt(landing.Time - 1e-3, Rate)).Should()
            .BeGreaterThan(0.25, "the box is still in the air just before");
    }

    [Fact]
    public void TheLineAtTheReleaseIsTheReleasePlaceAndItMovesAtTheReleaseVelocityThere()
    {
        var release = new ThrowRelease(Eye, Throw(1));

        release.PlaceAt(0, Rate).Should().Be(Eye);
        ((release.PlaceAt(1e-6, Rate) - release.PlaceAt(-1e-6, Rate)) / 2e-6 - Throw(1)).Length.Should().BeLessThan(
            1e-6,
            "the velocity relative to the turning axes at the release is the release velocity");
    }

    [Fact]
    public void AFlatFieldLandsABoxThatStartsAtItsHalfSizeAtOnceWithoutAVerticalSpeed()
    {
        var release = new ThrowRelease(new(-99.75, 0, 0), new(0, 0, 3));

        (double time, double spinward) = release.FlatLanding(9.81, Circle, 0.25);

        time.Should().BeApproximately(0, 1e-6);
        spinward.Should().BeApproximately(0, 1e-5);
    }
}
