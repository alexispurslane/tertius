using System.Numerics;
using Tertius.Simulation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Demo;

/// <summary>
/// The shape of the Blue Danube station in its own axes, from a few named sizes (docs/plans/blue-danube-demo.md, "The
/// kilometre wheel"). +Y is the axis, about which the station turns, and the angle θ about it runs from +X toward -Z. The
/// wheel's floor lies <see cref="FloorRadius"/> from the axis, and the station's rate gives one standard gravity there. The
/// hub is a box along the axis that holds the bay. The slot leads through its +Y wall, and its -X wall's inner face is the
/// deck. Two spokes, lift shafts, run along +X and -X in the plane y = 0, from the hub to the wheel's ceiling. The lift runs
/// in the spoke along -X. Every other place derives from these sizes, so a fit to the station's art moves the bay along the
/// axis or grows the bubble and the docking, the exit and the cameras follow.
/// <list type="bullet">
/// <item>The floor's gravity is <see cref="StandardGravity"/>, by <see cref="Rate"/>.</item>
/// <item>The deck's spin gravity is at least <see cref="MinDeckGravity"/>, so a person there keeps its feet.</item>
/// <item>The floor's facets sag less than <see cref="MaxFloorSag"/> below the circle of their joints.</item>
/// <item>The hub reaches across the spokes' width along the axis.</item>
/// <item>The bubble holds every corner of the hull, and leaves the hold point outside, each by more than the shuttle's reach.</item>
/// <item>The shuttle's frame fits in the station's, and its parked hull fits in the bay.</item>
/// <item>The ceiling's opening is narrower than the spoke's inside and wider than the lift.</item>
/// <item>The lift's hole in the deck lies inside the spoke and clear of the parked hull.</item>
/// </list>
/// <see cref="Problems"/> checks every invariant.
/// </summary>
public sealed record StationLayout
{
    /// <summary>Standard gravity, in m/s²: the floor's spin gravity.</summary>
    public const double StandardGravity = 9.80665;

    /// <summary>The least spin gravity on the deck, in m/s²: three times the zero-gravity threshold.</summary>
    public const double MinDeckGravity = 0.15;

    /// <summary>The most that a facet of the floor may sag below the circle through its joints, in meters.</summary>
    public const double MaxFloorSag = 0.02;

    /// <summary>The radius of the wheel's floor, its ring's inner face at the joints of its facets, in meters.</summary>
    public float FloorRadius { get; init; } = 2000;

    /// <summary>The thickness of the wheel's floor, in meters.</summary>
    public float FloorThickness { get; init; } = 1;

    /// <summary>Half the wheel's width along the axis, in meters: its hall is 60 m wide.</summary>
    public float HalfWidth { get; init; } = 30;

    /// <summary>How high the ceiling's face toward the axis lies above the floor, in meters.</summary>
    public float CeilingHeight { get; init; } = 20;

    /// <summary>The thickness of the wheel's ceiling, in meters.</summary>
    public float CeilingThickness { get; init; } = 0.5f;

    /// <summary>The thickness of each side wall of the wheel along the axis, in meters.</summary>
    public float SideWallThickness { get; init; } = 0.3f;

    /// <summary>The wheel's segments, and so the facets of its floor: 12.3 m long each, with a sag of 9.4 mm.</summary>
    public int Segments { get; init; } = 1024;

    /// <summary>
    /// How far the deck lies from the axis, in meters. Its spin gravity is ω² times this: 0.196 m/s² for 40 m. At 8 m it
    /// would be 0.039 m/s², below the zero-gravity threshold, where a person floats.
    /// </summary>
    public float DeckRadius { get; init; } = 40;

    /// <summary>The thickness of the hub's walls, in meters.</summary>
    public float HubWall { get; init; } = 0.5f;

    /// <summary>Half the length of the bay inside the walls along the axis, in meters.</summary>
    public float BayHalfAxial { get; init; } = 12.5f;

    /// <summary>Half the width of the bay inside the walls across the axis, along Z, in meters.</summary>
    public float BayHalfAcross { get; init; } = 8;

    /// <summary>Where the bay's middle lies along the axis, in meters, on the slot's side of the wheel's plane or in it.</summary>
    public float BayAxial { get; init; }

    /// <summary>Half the outside width of a spoke, in meters.</summary>
    public float SpokeHalfWidth { get; init; } = 8;

    /// <summary>The thickness of a spoke's walls, in meters.</summary>
    public float SpokeWall { get; init; } = 0.5f;

    /// <summary>
    /// The room that the slot leaves on each side of the shuttle's cross-section, in meters. A spin mismatch of 3 degrees
    /// moves a corner of the shuttle, 3.6 m from its axis, by 0.19 m, which this holds.
    /// </summary>
    public float SlotClearance { get; init; } = 1;

    /// <summary>How far the parked shuttle's tail lies inside the inner face of the slot's wall, in meters.</summary>
    public float ParkedTailGap { get; init; } = 1.5f;

    /// <summary>The bubble's radius across the axis, in meters.</summary>
    public float BubbleRadial { get; init; } = 2200;

    /// <summary>The bubble's radius along the axis, in meters.</summary>
    public float BubbleAxial { get; init; } = 120;

    /// <summary>How far beyond the bubble along the axis the hold point lies, in meters.</summary>
    public double HoldBeyondBubble { get; init; } = 30;

    /// <summary>Half the side of the lift's square floor, in meters: 4 m on a side.</summary>
    public float LiftHalf { get; init; } = 2;

    /// <summary>Where the middle of the lift's floor lies across the axis, along Z, in meters: spinward of the parked hull.</summary>
    public float LiftAcross { get; init; } = 2.5f;

    /// <summary>The gap round the lift's floor in its hole through the deck, in meters.</summary>
    public float LiftHoleGap { get; init; } = 0.05f;

    /// <summary>How high above the wheel's floor the lift's floor stops at the foot, in meters: a step down.</summary>
    public double LiftFootStep { get; init; } = 0.3;

    /// <summary>The station's rate about its axis, in rad/s: the rate that gives <see cref="StandardGravity"/> on the floor.</summary>
    public double Rate => Math.Sqrt(StandardGravity / FloorRadius);

    /// <summary>The x of the deck, the bay's -X wall's inner face, in meters.</summary>
    public float Deck => -DeckRadius;

    /// <summary>The radius of the ceiling's face toward the axis, in meters.</summary>
    public float CeilingRadius => FloorRadius - CeilingHeight;

    /// <summary>The wheel's floor, of <see cref="Segments"/> flat facets at <see cref="FloorRadius"/>.</summary>
    public WheelFloor WheelFloor => new(FloorRadius, Segments);

    /// <summary>The y of the slot's outer face, the hub's +Y face, in meters.</summary>
    public float SlotFace => BayAxial + BayHalfAxial + HubWall;

    /// <summary>
    /// The hub's outer box: across the axis, the deck and the bay's width with the walls; along the axis, from the slot's
    /// face back to the bay's far end or past the spokes, whichever lies farther.
    /// </summary>
    public BoundingBox Hub =>
        new(
            new(-(DeckRadius + HubWall), MathF.Min(BayAxial - BayHalfAxial, -SpokeHalfWidth) - HubWall, -(BayHalfAcross + HubWall)),
            new(DeckRadius + HubWall, SlotFace, BayHalfAcross + HubWall)
        );

    /// <summary>The bay, the hub inside its walls. Its air is the station's medium.</summary>
    public BoundingBox Bay
    {
        get
        {
            BoundingBox hub = Hub;
            return new(hub.Min + new Vector3(HubWall), hub.Max - new Vector3(HubWall));
        }
    }

    /// <summary>
    /// The slot through the hub's +Y wall, on the axis. The shuttle enters with its belly toward the deck, so its height
    /// lies along X and its width along Z, with <see cref="SlotClearance"/> on each side.
    /// </summary>
    public BoundingBox Slot =>
        new(
            new(-Shuttle.Hull.Y - SlotClearance, SlotFace - HubWall, -Shuttle.Hull.X - SlotClearance),
            new(Shuttle.Hull.Y + SlotClearance, SlotFace, Shuttle.Hull.X + SlotClearance)
        );

    /// <summary>
    /// Where the docked shuttle stands parked, with <see cref="DockedRotation"/>: belly on the deck, port side on the bay's
    /// -Z wall, and tail <see cref="ParkedTailGap"/> inside the slot's wall.
    /// </summary>
    public DPose ParkedPose =>
        new(
            new(Deck + Shuttle.Hull.Y, Bay.Max.Y - ParkedTailGap - Shuttle.Hull.Z, Bay.Min.Z + Shuttle.Hull.X),
            DockedRotation
        );

    /// <summary>The box of the parked shuttle: the hull's box, <see cref="Shuttle.Hull"/>, at <see cref="ParkedPose"/>.</summary>
    public BoundingBox ParkedBounds => BoxAt(ParkedPose, Shuttle.Hull);

    /// <summary>
    /// The shuttle's rotation in the station's axes once it is docked: its nose, -Z, along the station's -Y, into the bay,
    /// and its roof, +Y, along +X, away from the deck. So its belly faces the deck, and its starboard side, +X, with the
    /// hatch, faces +Z.
    /// </summary>
    public static DQuaternion DockedRotation { get; } = AttitudeAutopilot.Facing(-Double3.UnitY, Double3.UnitX);

    /// <summary>The hold point: on the axis, <see cref="HoldBeyondBubble"/> beyond the bubble on the slot's side.</summary>
    public Double3 HoldPoint => new(0, BubbleAxial + HoldBeyondBubble, 0);

    /// <summary>The station's bubble: an ellipsoid with <see cref="BubbleRadial"/> across the axis and <see cref="BubbleAxial"/> along it.</summary>
    public FrameBounds Bubble => new(new(BubbleRadial, BubbleAxial, BubbleRadial));

    /// <summary>
    /// Half the width of the ceiling's opening under the lift's spoke at its face toward the axis, in meters: the opening
    /// is one segment left out, turned half a segment, so it spans the angle of one segment centered on the spoke.
    /// </summary>
    public float CeilingOpeningHalf => CeilingRadius * MathF.Sin(MathF.PI / Segments);

    /// <summary>The lift's floor across the axis, in its place at the top, flush with the deck: y and z only, x through the deck's wall.</summary>
    public BoundingBox LiftFootprint =>
        new(new(Deck - HubWall, -LiftHalf, LiftAcross - LiftHalf), new(Deck, LiftHalf, LiftAcross + LiftHalf));

    /// <summary>The lift's hole through the deck's wall: its footprint with <see cref="LiftHoleGap"/> round it.</summary>
    public BoundingBox LiftHole
    {
        get
        {
            BoundingBox lift = LiftFootprint;
            var gap = new Vector3(0, LiftHoleGap, LiftHoleGap);
            return new(lift.Min - gap, lift.Max + gap);
        }
    }

    /// <summary>
    /// How far from the axis along the spoke the lift's floor stops at the foot, in meters: <see cref="LiftFootStep"/> above
    /// the facet of the wheel's floor under the lift's middle.
    /// </summary>
    public double LiftFoot
    {
        get
        {
            WheelFloor floor = WheelFloor;
            Double3 normal = floor.Normal(new Double3(-FloorRadius, 0, LiftAcross));
            // The floor's plane holds the points p with p · n = the facet's distance. Along the spoke, p = (-d, 0, z).
            return (LiftAcross * normal.Z - floor.FacetDistance) / normal.X - LiftFootStep;
        }
    }

    /// <summary>
    /// The corners of the hull: the wheel's rim and its ceiling's edges, across the axis at θ = 0, and the hub's corners.
    /// The spokes lie inside these.
    /// </summary>
    public IEnumerable<Double3> HullCorners()
    {
        foreach (double radius in new double[] { FloorRadius + FloorThickness, CeilingRadius })
        foreach (double y in new double[] { -HalfWidth, HalfWidth })
            yield return new(radius, y, 0);
        BoundingBox hub = Hub;
        foreach (int corner in Enumerable.Range(0, 8))
            yield return new(
                (corner & 1) == 0 ? hub.Min.X : hub.Max.X,
                (corner & 2) == 0 ? hub.Min.Y : hub.Max.Y,
                (corner & 4) == 0 ? hub.Min.Z : hub.Max.Z
            );
    }

    /// <summary>The broken invariants of this layout (see the type's list), each as one sentence. It is empty for a valid layout.</summary>
    public IEnumerable<string> Problems()
    {
        double deckGravity = Rate * Rate * DeckRadius,
            sag = FloorRadius - WheelFloor.FacetDistance,
            reach = Shuttle.Hull.Length();
        float spokeInside = SpokeHalfWidth - SpokeWall;
        BoundingBox hub = Hub,
            bay = Bay,
            lift = LiftFootprint,
            hole = LiftHole,
            parked = ParkedBounds;
        if (deckGravity < MinDeckGravity)
            yield return $"The deck's spin gravity, {deckGravity:F3} m/s², is below {MinDeckGravity} m/s².";
        if (sag >= MaxFloorSag)
            yield return $"The floor's facets sag {sag:F4} m, not less than {MaxFloorSag} m.";
        if (hub.Min.Y > -SpokeHalfWidth || hub.Max.Y < SpokeHalfWidth)
            yield return "The hub does not reach across the spokes' width along the axis.";
        foreach (Double3 corner in HullCorners().Where(corner => Bubble.SignedDistance(corner) >= -reach))
            yield return $"The hull's corner {corner} lies less than the shuttle's reach inside the bubble.";
        if (Bubble.SignedDistance(HoldPoint) <= reach)
            yield return $"The hold point {HoldPoint} lies less than the shuttle's reach outside the bubble.";
        if (!FrameBounds.Fits(new FrameBounds(Shuttle.Bubble), Bubble))
            yield return "The shuttle's frame does not fit in the station's.";
        if (!(bay.Contains(parked.Min + new Vector3(1e-3f)) && bay.Contains(parked.Max - new Vector3(1e-3f))))
            yield return "The parked hull does not fit in the bay.";
        if (CeilingOpeningHalf >= spokeInside)
            yield return "The ceiling's opening is wider than the spoke's inside.";
        if (lift.Min.Z <= -CeilingOpeningHalf || lift.Max.Z >= CeilingOpeningHalf)
            yield return "The lift does not fit through the ceiling's opening.";
        if (hole.Min.Y <= -spokeInside || hole.Max.Y >= spokeInside || hole.Min.Z <= -spokeInside || hole.Max.Z >= spokeInside)
            yield return "The lift's hole does not lie inside the spoke.";
        bool overlaps =
            hole.Min.Y < parked.Max.Y && hole.Max.Y > parked.Min.Y && hole.Min.Z < parked.Max.Z && hole.Max.Z > parked.Min.Z;
        if (overlaps)
            yield return "The lift's hole lies under the parked hull.";
    }

    /// <summary>The axis-aligned box that holds the box of half extents <paramref name="half"/> at <paramref name="pose"/>.</summary>
    private static BoundingBox BoxAt(DPose pose, Vector3 half)
    {
        Vector3 min = new(float.MaxValue),
            max = new(float.MinValue);
        foreach (int corner in Enumerable.Range(0, 8))
        {
            var local = new Double3(
                (corner & 1) == 0 ? -half.X : half.X,
                (corner & 2) == 0 ? -half.Y : half.Y,
                (corner & 4) == 0 ? -half.Z : half.Z
            );
            var at = pose.TransformPoint(local).ToVector3();
            min = Vector3.Min(min, at);
            max = Vector3.Max(max, at);
        }

        return new(min, max);
    }
}
