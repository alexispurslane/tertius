namespace Tertius.Rendering;

/// <summary>The renderer draws no map this frame.</summary>
internal readonly record struct WithoutMap;

/// <summary>
/// The faces that this frame draws of each served map, root first, and the latest irradiance reading of each, null before
/// its first (docs/plans/environment-maps-per-frame.md, "Which faces a frame draws"). Both hold M entries, one per served
/// map.
/// </summary>
internal readonly record struct WithMap(ReadOnlyMemory<FaceMask> Faces, ReadOnlyMemory<IrradianceReading?> Irradiance);

/// <summary>
/// What the batcher prepares for the map this frame (docs/plans/environment-map.md, "Which faces a frame draws"). Its
/// zero value is <see cref="WithoutMap"/>. This is a generated union (docs/plans/union-tooling.md).
/// </summary>
[GeneratedUnion(typeof(WithoutMap), typeof(WithMap), ZeroCase = typeof(WithoutMap))]
internal readonly partial struct EnvironmentDraw;

/// <summary>The hemisphere of the snapshot is the ambient light.</summary>
internal readonly record struct HemisphereAmbient(AmbientLight Light);

/// <summary>
/// The maps are the ambient light; the CPU knows the irradiance of each served map, root first, from its latest reading,
/// null before its first.
/// </summary>
internal readonly record struct MapAmbient(ReadOnlyMemory<IrradianceReading?> Irradiance);

/// <summary>
/// The ambient light of a frame as the CPU knows it (docs/plans/environment-map.md, "Impostors"). This is a generated
/// union (docs/plans/union-tooling.md).
/// </summary>
[GeneratedUnion(typeof(HemisphereAmbient), typeof(MapAmbient))]
internal readonly partial struct AmbientSource;

/// <summary>
/// One face of a map that this frame draws, with its axes in world axes, <see cref="CubeFaces.Basis(CubeFace, System.Numerics.Quaternion)"/>
/// of the map's axes, and its opaque commands per depth range: the solid run and the cutout run of each.
/// </summary>
internal readonly record struct EnvironmentFaceDraw(
    int Map,
    CubeFace Face,
    FaceBasis Basis,
    OpaqueRuns Far,
    OpaqueRuns Middle,
    OpaqueRuns Near
)
{
    /// <summary>The commands of depth range <paramref name="range"/>, indexed by <see cref="DepthRanges"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="range"/> is not a depth range.</exception>
    public OpaqueRuns this[int range] =>
        range switch
        {
            DepthRanges.Far => Far,
            DepthRanges.Middle => Middle,
            DepthRanges.Near => Near,
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, "A depth range is 0, 1 or 2."),
        };
}
