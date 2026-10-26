using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// No map: the hemisphere of World.Ambient is the ambient light, as before the map. The renderer makes no map images and no
/// map passes.
/// </summary>
public readonly record struct NoEnvironmentMap;

/// <summary>
/// A cube map of the scene around the camera (docs/plans/environment-map.md): the three depth ranges lit by the sun and the
/// lamps, and the share of each texel through which the sky is open. It gives every surface its ambient light, diffuse and
/// specular, the stars in its reflections, and SSR its fallback.
/// </summary>
/// <param name="FaceSize">The texels along one side of each face: a power of two from 16 to 1024.</param>
/// <param name="FacesPerFrame">How many of the six faces each drawn frame draws again, 1 to 6. 6 keeps every face from the frame that reads it.</param>
public readonly record struct SceneEnvironmentMap(int FaceSize, int FacesPerFrame)
{
    /// <summary>The renderer's default: 128 texels per face, one face per frame.</summary>
    public static SceneEnvironmentMap Default => new(FaceSize: 128, FacesPerFrame: 1);
}

/// <summary>
/// Whether the renderer lights surfaces with a map of the scene around the camera: <see cref="NoEnvironmentMap"/> or
/// <see cref="SceneEnvironmentMap"/>. The default value is NoEnvironmentMap. It is a generated union (see
/// <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(NoEnvironmentMap), typeof(SceneEnvironmentMap), ZeroCase = typeof(NoEnvironmentMap))]
public readonly partial struct EnvironmentMapSetting
{
    /// <summary>The smallest face: its levels reach roughness 1 through one level between, 16, 8 and 4 texels.</summary>
    public const int MinFaceSize = 16;

    /// <summary>The largest face. At 1024 texels one texel is about the width of the lobe of the roughness floor, so a larger map adds nothing that any material shows.</summary>
    public const int MaxFaceSize = 1024;

    /// <summary>The most faces a frame can draw: all six.</summary>
    public const int MaxFacesPerFrame = 6;

    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            NoEnvironmentMap => null,
            SceneEnvironmentMap map => ProblemOf(map),
        };

    private static string? ProblemOf(SceneEnvironmentMap map)
    {
        bool sizeInRange = map.FaceSize is >= MinFaceSize and <= MaxFaceSize && BitOperations.IsPow2(map.FaceSize);
        if (!sizeInRange)
            return $"SceneEnvironmentMap.FaceSize must be a power of two from {MinFaceSize} to {MaxFaceSize}, not {map.FaceSize}.";
        if (map.FacesPerFrame is < 1 or > MaxFacesPerFrame)
            return $"SceneEnvironmentMap.FacesPerFrame must be 1 to {MaxFacesPerFrame}, not {map.FacesPerFrame}.";
        return null;
    }
}
