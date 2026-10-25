namespace Tertius.Rendering;

/// <summary>The renderer draws no map this frame.</summary>
internal readonly record struct WithoutMap;

/// <summary>The faces that this frame draws, and the latest irradiance reading, null before the first.</summary>
internal readonly record struct WithMap(FaceMask Faces, IrradianceReading? Irradiance);

/// <summary>
/// What the batcher prepares for the map this frame (docs/plans/environment-map.md, "Which faces a frame draws"). Its
/// zero value is <see cref="WithoutMap"/>. This is a generated union (docs/plans/union-tooling.md).
/// </summary>
[GeneratedUnion(typeof(WithoutMap), typeof(WithMap), ZeroCase = typeof(WithoutMap))]
internal readonly partial struct EnvironmentDraw;

/// <summary>The hemisphere of the snapshot is the ambient light.</summary>
internal readonly record struct HemisphereAmbient(AmbientLight Light);

/// <summary>The map is the ambient light; the CPU knows its irradiance from the latest reading, null before the first.</summary>
internal readonly record struct MapAmbient(IrradianceReading? Irradiance);

/// <summary>
/// The ambient light of a frame as the CPU knows it (docs/plans/environment-map.md, "Impostors"). This is a generated
/// union (docs/plans/union-tooling.md).
/// </summary>
[GeneratedUnion(typeof(HemisphereAmbient), typeof(MapAmbient))]
internal readonly partial struct AmbientSource;

/// <summary>One face of the map that this frame draws, with its opaque commands per depth range.</summary>
internal readonly record struct EnvironmentFaceDraw(
    CubeFace Face,
    CommandRange Far,
    CommandRange Middle,
    CommandRange Near
)
{
    /// <summary>The commands of depth range <paramref name="range"/>, indexed by <see cref="DepthRanges"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="range"/> is not a depth range.</exception>
    public CommandRange this[int range] =>
        range switch
        {
            DepthRanges.Far => Far,
            DepthRanges.Middle => Middle,
            DepthRanges.Near => Near,
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, "A depth range is 0, 1 or 2."),
        };
}
