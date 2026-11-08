using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The spread of a point or tube light: every direction, from a capsule around the segment from Position − HalfSegment to
/// Position + HalfSegment (ARCHITECTURE.md, "Tube lights"). A point light's segment is zero, which leaves its sphere.
/// </summary>
/// <param name="HalfSegment">From the light's position to one end of its segment, camera-relative, in m; zero for a point light.</param>
internal readonly record struct EveryDirection(Vector3 HalfSegment);

/// <summary>
/// A spot light's spread: a cone around a unit axis. Build it with <see cref="Of"/>, which keeps its invariants:
/// <list type="bullet">
/// <item><see cref="OuterAngle"/> is in [LightUnits.MinConeAngle, π].</item>
/// <item>CosInner ≥ CosOuter + min(10^−4, (1 − CosOuter) / 2), so smoothstep's edges differ and the axis is inside the inner edge.</item>
/// </list>
/// </summary>
/// <param name="Axis">The unit axis of the cone, camera-relative, interpolated.</param>
/// <param name="CosInner">The cosine of the inner half-angle, inside which the light is full.</param>
/// <param name="CosOuter">The cosine of the outer half-angle, outside which the light adds nothing.</param>
/// <param name="OuterAngle">The outer half-angle, in rad.</param>
internal readonly record struct Cone(Vector3 Axis, float CosInner, float CosOuter, float OuterAngle)
{
    /// <summary>The widest fade between the two edges, in cosine: today's edge for a cone wider than about 0.014 rad.</summary>
    private const float WidestFade = 1e-4f;

    /// <summary>
    /// The cone of a spot light with the unit <paramref name="axis"/>, its half-angles clamped, and its inner edge moved
    /// inward where it lies too near the outer edge or beyond it (docs/plans/local-light-shadows.md, "Portable Vulkan").
    /// </summary>
    public static Cone Of(Vector3 axis, float innerAngle, float outerAngle)
    {
        Contract.Requires(MathF.Abs(axis.LengthSquared() - 1) < 1e-4f);
        float outer = Math.Clamp(outerAngle, LightUnits.MinConeAngle, MathF.PI);
        float cosOuter = MathF.Cos(outer);
        // The fade takes at most half of the cone's cosines, so the axis, at cosine 1, gets the whole intensity, which
        // SpotIntensity spreads over the whole cone.
        float fade = MathF.Min(WidestFade, (1 - cosOuter) / 2);
        float cosInner = MathF.Max(MathF.Cos(Math.Clamp(innerAngle, 0f, MathF.PI)), cosOuter + fade);
        return new(Axis: axis, CosInner: cosInner, CosOuter: cosOuter, OuterAngle: outer);
    }
}

/// <summary>
/// Which directions a light sends its power to: <see cref="EveryDirection"/> for a point or tube light, a <see cref="Cone"/>
/// for a spot light. The default value is EveryDirection with no segment. This is a generated union
/// (docs/plans/union-tooling.md).
/// </summary>
[GeneratedUnion(typeof(EveryDirection), typeof(Cone), ZeroCase = typeof(EveryDirection))]
internal readonly partial struct LightSpread;

/// <summary>
/// Whether the main view keeps a light, and the tier in which every shadow budget ranks it: a higher value ranks first,
/// and inside a tier the higher <see cref="LightUnits.RankIrradiance"/> ranks first, ties in list order
/// (docs/plans/local-light-shadows.md, decision 24). The zero value is FaceOnly.
/// </summary>
internal enum MainViewTier : byte
{
    /// <summary>
    /// Only a drawn face of a map keeps the light. It ranks last, so it cannot take a slot from a light of the main view
    /// and change the main view's pixels (docs/plans/environment-map.md, decision 20).
    /// </summary>
    FaceOnly,

    /// <summary>
    /// The main view keeps a spot light's cone, cut at its reach, but its axis, cut at its reach, misses the view. Its
    /// light is strongest along that axis, so it ranks below every light whose light falls in view (the user's design B
    /// of ter-19fm: "the lowest rank among lights in the main view"). A lamp whose beam only clips the view's edge must
    /// not take a tile from one that lights the floor ahead. The one exception is a holder of tiles, which ranks in the
    /// tier of a longer reach (<see cref="ShadowTiles.HolderMargin"/>): a holder of this tier whose axis enters the view at
    /// that reach keeps its tiles against a lamp that lights the floor ahead with less than twice its light. A wide inner
    /// cone can still put full light in view: a known limit of the rule (ARCHITECTURE.md, "Local shadows", the rank).
    /// </summary>
    AxisMissesView,

    /// <summary>
    /// The main view keeps the light, and its light falls in view: a point light whose reach sphere reaches the view, or a
    /// spot light whose axis, cut at its reach, enters the view.
    /// </summary>
    InView,
}

/// <summary>One light of a frame after interpolation and culling, before its GPU form: the analogue of PreparedInstance.</summary>
internal struct PreparedLight
{
    /// <summary>
    /// The entity that carries the light, its slot and generation. The slot names the light within one snapshot, and the
    /// whole handle follows it from frame to frame, because a new entity can take the slot of a destroyed one.
    /// </summary>
    public Entity Entity;

    /// <summary>Camera-relative position, interpolated.</summary>
    public Vector3 Position;

    /// <summary>The color × intensity, in W/sr per channel, from <see cref="LightUnits.RadiantIntensity"/>.</summary>
    public Vector3 RadiantIntensity;

    /// <summary>The radius of the emitting sphere, or of a tube light's capsule, in m.</summary>
    public float Radius;

    /// <summary>
    /// Where the brightest channel falls to the cutoff irradiance, in m from <see cref="Position"/>: for a tube light, its
    /// peak intensity's reach from its segment plus half its length.
    /// </summary>
    public float Reach;

    /// <summary>The distance from the eye, in m.</summary>
    public double Distance;

    public bool CastsShadow;

    /// <summary>
    /// Whether the main view keeps the light, and the tier in which the shadow budgets rank it (<see cref="MainViewTier"/>).
    /// The batcher sets it with <see cref="RankDistance"/>, from the same test.
    /// </summary>
    public MainViewTier Tier;

    /// <summary>
    /// Whether the light can reach the main view's frustum: a point light's reach sphere, or a spot light's cone cut at its
    /// reach (<see cref="CameraView.Culls(Vector3, in Cone, float)"/>). False for a light that only a drawn face of a map keeps:
    /// the main view's shaders and its impostors' light signatures leave it out (docs/plans/environment-map.md, decision
    /// 20). Derived from <see cref="Tier"/>.
    /// </summary>
    public readonly bool InMainView => Tier != MainViewTier.FaceOnly;

    /// <summary>
    /// The distance D in m from the eye to the light's spot of light, by which the shadow budgets rank it inside its
    /// <see cref="Tier"/> (<see cref="LightUnits.RankIrradiance"/>), because a spot of light D from the eye shows on screen
    /// in proportion to 1 / D². For a spot light of <see cref="MainViewTier.InView"/>, the eye's distance to the point
    /// nearest the eye of its axis, cut at its reach, inside the view. For a spot light of
    /// <see cref="MainViewTier.AxisMissesView"/>, its reach, so that it ranks inside its tier by its light at its reach:
    /// luminance(I) / reach², which is E_cut × luminance(I) / peak channel(I), because the reach follows the peak channel and
    /// the rank the luminance. For a white lamp that is E_cut. For any other light, the eye's distance to the light,
    /// <see cref="Distance"/>.
    /// </summary>
    public double RankDistance;

    public LightSpread Spread;

    /// <summary>How the light casts shadows in this frame. The default holds none.</summary>
    public LightShadow Shadow;

    /// <summary>The light's map, <see cref="EnvironmentChain.MapOf"/> of its chain depth, or 0 with the map off.</summary>
    public int Map;
}
