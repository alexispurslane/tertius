using Tertius.Demo;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;

namespace Tertius.Host.Tests;

/// <summary>
/// The wheel's floor as the coda reads it (docs/plans/blue-danube-demo.md, "The coda"): a ring of flat facets in the
/// station's axes, with the angle θ about +Y from +X toward -Z, the way of the station's turn, so spinward is the way of
/// growing θ. Pure, with no World.
/// </summary>
public sealed class WheelFloorTests
{
    private static WheelFloor Floor => DemoStation.WheelFloor;

    [Fact]
    public void TheDemosFloorIsTheWheelsRingOf256FacetsAt100Meters()
    {
        Floor.Radius.Should().Be(DemoStation.FloorRadius);
        Floor.Segments.Should().Be(DemoStation.Segments);
        Floor.FacetDistance.Should().BeApproximately(100 * Math.Cos(Math.PI / 256), 1e-12,
            "a facet's middle lies inside the ring's circle");
    }

    [Fact]
    public void APlaceOnAFacetLiesItsHeightAboveThatFacetAndAlongItsSpinwardWay()
    {
        Double3 place = Floor.OnFacet(facet: 130, height: 0.25, y: -2, along: 0.5);

        Floor.FacetIndex(place).Should().Be(130);
        Floor.Height(place).Should().BeApproximately(0.25, 1e-9);
        place.Y.Should().Be(-2);
        Floor.Spinward(Floor.OnFacet(facet: 130, height: 0.25, y: -2), place).Should().BeApproximately(
            Floor.Radius * Math.Atan2(0.5, Floor.FacetDistance - 0.25), 1e-9,
            "half a meter along the facet, 0.25 m above it, turns the place about the axis by the angle that it spans there");
    }

    [Fact]
    public void SpinwardIsTheWayOfTheStationsTurnAtTheChute()
    {
        // At θ = 180 degrees, under the spoke of the chute, the station's point moves toward +Z.
        var under = new Double3(-99.9, 0, 0);
        WheelFloor.SpinwardAt(under).Should().Be(new Double3(0, 0, 1));
        WheelFloor.UpAt(under).Should().Be(new Double3(1, 0, 0));
        Floor.Spinward(under, new(-99.9, 3, 0.5)).Should().BeApproximately(Floor.Radius * Math.Atan2(0.5, 99.9), 1e-9);
        Floor.Spinward(under, new(-99.9, 0, -0.5)).Should().BeNegative("-Z is antispinward there");
    }

    [Fact]
    public void TheSpinwardDistanceCrossesTheAngleZeroTheShortWay()
    {
        Double3 before = Floor.OnFacet(facet: Floor.Segments - 1, height: 0, y: 0), after = Floor.OnFacet(facet: 0, height: 0, y: 0);

        Floor.Spinward(before, after).Should().BeApproximately(Floor.Facet * Floor.Radius, 1e-9);
        Floor.Spinward(after, before).Should().BeApproximately(-Floor.Facet * Floor.Radius, 1e-9);
    }

    [Fact]
    public void AFacetsTurnLaysABoxFlatOnIt()
    {
        // A box turned by the facet's turn has its +X along the facet's outward normal.
        (Floor.FacetTurn(77).Rotate(Double3.UnitX) - Floor.Normal(77)).Length.Should().BeLessThan(1e-12);
        (Floor.Normal(77) - Floor.Normal(Floor.OnFacet(facet: 77, height: 1, y: 4))).Length.Should().BeLessThan(1e-12);
    }
}
