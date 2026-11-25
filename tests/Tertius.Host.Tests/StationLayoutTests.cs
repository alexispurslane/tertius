using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The layout of the Blue Danube station, <see cref="StationLayout"/> (docs/plans/blue-danube-demo.md, "The kilometre
/// wheel"), as pure geometry in the station's axes: +Y is the axis, the slot faces +Y, and the deck is the bay's -X wall.
/// The demo's layout puts the floor 2,000 m out at one standard gravity. A later fit to the station's art moves the bay
/// out along the axis and grows the bubble, so the same invariants also hold for a layout with the bay 300 m out.
/// </summary>
public sealed class StationLayoutTests
{
    private static readonly StationLayout Demo = new();

    /// <summary>A layout for the fit to the art: the bay 300 m out along the axis and the bubble 450 m along it.</summary>
    private static readonly StationLayout Moved = new() { BayAxial = 300, BubbleAxial = 450 };

    public static TheoryData<string> Layouts => ["demo", "moved"];

    private static StationLayout Named(string name) => name == "demo" ? Demo : Moved;

    /// <summary>The demo's floor lies 2,000 m from the axis, and its rate gives one standard gravity there, 9.80665 m/s².</summary>
    [Fact]
    public void TheDemosFloorGivesOneStandardGravity()
    {
        Demo.FloorRadius.Should().Be(2000);
        (Demo.Rate * Demo.Rate * Demo.FloorRadius).Should().BeApproximately(9.80665, 1e-12);
        Demo.Rate.Should().BeApproximately(0.0700237, 1e-7);
    }

    /// <summary>
    /// The deck's spin gravity, ω² times its distance from the axis, is at least 0.15 m/s², above twice the zero-gravity
    /// threshold, so a person on the deck keeps the walking form (ARCHITECTURE.md, "Characters").
    /// </summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheDecksSpinGravityKeepsAPersonOnItsFeet(string name)
    {
        StationLayout layout = Named(name);
        double gravity = layout.Rate * layout.Rate * layout.DeckRadius;
        gravity.Should().BeGreaterThanOrEqualTo(0.15);
        gravity.Should().BeGreaterThan(2 * FrameTree.ZeroGravityThreshold);
    }

    /// <summary>The floor's facets sag less than 2 cm below the circle through their joints.</summary>
    [Fact]
    public void TheFloorsFacetsSagLessThanTwoCentimeters() =>
        (Demo.FloorRadius - Demo.WheelFloor.FacetDistance).Should().BeLessThan(0.02);

    /// <summary>
    /// The parked hull touches the deck with its belly and the bay's -Z wall with its port side, and its tail lies the
    /// parked tail gap inside the slot wall's inner face.
    /// </summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheParkedHullLiesInTheCornerOfTheDeckAndTheWall(string name)
    {
        StationLayout layout = Named(name);
        BoundingBox parked = layout.ParkedBounds;

        parked.Min.X.Should().BeApproximately(layout.Deck, 1e-4f);
        parked.Min.Z.Should().BeApproximately(layout.Bay.Min.Z, 1e-4f);
        parked.Max.Y.Should().BeApproximately(layout.Bay.Max.Y - layout.ParkedTailGap, 1e-4f);
        var grown = new BoundingBox(layout.Bay.Min - new Vector3(1e-3f), layout.Bay.Max + new Vector3(1e-3f));
        grown.Contains(parked.Min).Should().BeTrue();
        grown.Contains(parked.Max).Should().BeTrue();
    }

    /// <summary>
    /// The spin gravity at the parked hull's center points away from the axis, into the deck and into the -Z wall, so the
    /// corner holds the hull. It leans about 7.5 degrees from the deck's normal toward the wall.
    /// </summary>
    [Fact]
    public void TheSpinGravityPressesTheParkedHullIntoTheDeckAndTheWall()
    {
        Double3 center = Demo.ParkedPose.Position;
        var gravity = new Double3(center.X, 0, center.Z);

        gravity.X.Should().BeNegative("the deck is the bay's -X wall");
        gravity.Z.Should().BeNegative("the hull rests on the bay's -Z wall");
        (Math.Atan2(-gravity.Z, -gravity.X) * 180 / Math.PI).Should().BeApproximately(7.5, 0.1);
    }

    /// <summary>The bay's middle, the slot, the parked hull and the hold point move out along the axis with the bay.</summary>
    [Fact]
    public void TheBaysPartsFollowItsPlaceOnTheAxis()
    {
        float shift = Moved.BayAxial - Demo.BayAxial;

        (Moved.Slot.Min.Y - Demo.Slot.Min.Y).Should().BeApproximately(shift, 1e-3f);
        (Moved.ParkedPose.Position.Y - Demo.ParkedPose.Position.Y).Should().BeApproximately(shift, 1e-3);
        Moved.Bay.Max.Y.Should().BeApproximately(Demo.Bay.Max.Y + shift, 1e-3f);
        Moved.HoldPoint.Y.Should().Be(Moved.BubbleAxial + Moved.HoldBeyondBubble);
    }

    /// <summary>The demo's hold point lies 150 m out on the axis, 30 m beyond its bubble.</summary>
    [Fact]
    public void TheDemosHoldPointLiesOneHundredFiftyMetersOut() =>
        Demo.HoldPoint.Should().Be(new Double3(0, 150, 0));

    /// <summary>
    /// The hold point lies outside the bubble by more than the shuttle's bounding radius, so the shuttle spins up outside the
    /// station's frame.
    /// </summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheHoldPointLiesOutsideTheBubble(string name)
    {
        StationLayout layout = Named(name);
        layout.Bubble.SignedDistance(layout.HoldPoint).Should().BeGreaterThan(Shuttle.Hull.Length());
    }

    /// <summary>
    /// Every corner of the hull, the wheel's rim, the spokes' ends and the hub's corners, lies inside the bubble by more than
    /// the shuttle's bounding radius, so the shuttle enters the frame before it reaches the hull.
    /// </summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheBubbleHoldsTheWholeHull(string name)
    {
        StationLayout layout = Named(name);
        foreach (Double3 corner in layout.HullCorners())
            layout.Bubble.SignedDistance(corner).Should().BeLessThan(-Shuttle.Hull.Length(), $"for the corner {corner}");
    }

    /// <summary>The shuttle's frame fits in the station's, so it can enter it as a child.</summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheShuttlesFrameFitsInTheStations(string name) =>
        FrameBounds.Fits(new FrameBounds(Shuttle.Bubble), Named(name).Bubble).Should().BeTrue();

    /// <summary>
    /// The ceiling's opening under the lift's spoke is narrower than the spoke's inside, so the spoke's tube closes it all
    /// round, and the lift with its railings fits through it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheCeilingsOpeningLiesInsideTheSpokeAndHoldsTheLift(string name)
    {
        StationLayout layout = Named(name);
        float inside = layout.SpokeHalfWidth - layout.SpokeWall;
        BoundingBox lift = layout.LiftFootprint;

        layout.CeilingOpeningHalf.Should().BeLessThan(inside);
        lift.Min.Z.Should().BeGreaterThan(-layout.CeilingOpeningHalf);
        lift.Max.Z.Should().BeLessThan(layout.CeilingOpeningHalf);
        lift.Min.Y.Should().BeGreaterThan(-inside);
        lift.Max.Y.Should().BeLessThan(inside);
    }

    /// <summary>The lift's hole in the deck lies inside the spoke and clear of the parked hull.</summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheLiftsHoleLiesInsideTheSpokeAndClearOfTheParkedHull(string name)
    {
        StationLayout layout = Named(name);
        BoundingBox hole = layout.LiftHole;
        float inside = layout.SpokeHalfWidth - layout.SpokeWall;

        hole.Min.Y.Should().BeGreaterThan(-inside);
        hole.Max.Y.Should().BeLessThan(inside);
        hole.Min.Z.Should().BeGreaterThan(-inside);
        hole.Max.Z.Should().BeLessThan(inside);
        bool overlaps =
            hole.Min.Y < layout.ParkedBounds.Max.Y
            && hole.Max.Y > layout.ParkedBounds.Min.Y
            && hole.Min.Z < layout.ParkedBounds.Max.Z
            && hole.Max.Z > layout.ParkedBounds.Min.Z;
        overlaps.Should().BeFalse();
    }

    /// <summary>The hub reaches across the spokes' width along the axis, so each spoke meets its outer face.</summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheHubReachesAcrossTheSpokes(string name)
    {
        StationLayout layout = Named(name);
        layout.Hub.Min.Y.Should().BeLessThanOrEqualTo(-layout.SpokeHalfWidth);
        layout.Hub.Max.Y.Should().BeGreaterThanOrEqualTo(layout.SpokeHalfWidth);
    }

    /// <summary>Every invariant of the layout holds for the demo's layout and for the moved one.</summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheLayoutHasNoProblems(string name) => Named(name).Problems().Should().BeEmpty();

    /// <summary>A bubble too short along the axis to hold the hub before the slot is a problem of the layout.</summary>
    [Fact]
    public void ABubbleThatEndsBeforeTheSlotIsAProblem() =>
        (Moved with { BubbleAxial = 200 }).Problems().Should().NotBeEmpty();

    /// <summary>A deck so near the axis that a person there floats is a problem of the layout.</summary>
    [Fact]
    public void ADeckWhereAPersonFloatsIsAProblem() =>
        (Demo with { DeckRadius = 8 }).Problems().Should().NotBeEmpty();
}
