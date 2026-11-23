// PROBE (ter-yglw, never commit to src): the environment face cost's scratch switches and per-face counts.
using System.Globalization;

namespace Tertius.Rendering;

/// <summary>
/// Scratch switches of ter-yglw, read once from the environment:
/// <list type="bullet">
/// <item>TERTIUS_PROBE_FACE_SIZE: the face size W of the default environment map (a power of two), 128 when unset.</item>
/// <item>TERTIUS_PROBE_FACES_PER_FRAME: the faces that each face frame draws, 1 when unset.</item>
/// <item>TERTIUS_PROBE_FACE_CULL=1: a face leaves out an instance whose bounding sphere spans less than one texel at the
/// face's center, seen from the map's capture point (the eye): radius / distance below sin(atan(1 / W)), the half
/// angle of the widest texel. An instance that holds the eye (distance at most the radius) is never culled.</item>
/// <item>TERTIUS_PROBE_FACE_EVERY=n: the in-turn faces are drawn only in every n-th drawn frame, slot by slot
/// (frame / n), so every face of every map still comes in turn, and the other frames draw none. A refresh of all faces
/// stays as it is. 1 when unset.</item>
/// <item>TERTIUS_PROBE_FACE_LOG=path: one line per drawn frame: the frame number, then per drawn face
/// "map:face:commands:triangles:kept:culled", where commands is the face's indirect draw commands over its three
/// depth ranges, triangles the sum over its kept instances of their mesh's triangles times their depth ranges, kept the
/// instances it draws, and culled the instances that only the size cull left out.</item>
/// </list>
/// </summary>
internal static class FaceProbe
{
    public static readonly int Size = int.TryParse(
        Environment.GetEnvironmentVariable("TERTIUS_PROBE_FACE_SIZE"),
        CultureInfo.InvariantCulture,
        out int size
    )
        ? size
        : 128;

    public static readonly bool Cull = Environment.GetEnvironmentVariable("TERTIUS_PROBE_FACE_CULL") == "1";

    /// <summary>TERTIUS_PROBE_FACES_PER_FRAME: the default map's faces per drawn frame, 1 when unset.</summary>
    public static readonly int PerFrame = int.TryParse(
        Environment.GetEnvironmentVariable("TERTIUS_PROBE_FACES_PER_FRAME"),
        CultureInfo.InvariantCulture,
        out int perFrame
    )
        ? perFrame
        : 1;

    public static readonly int Every = int.TryParse(
        Environment.GetEnvironmentVariable("TERTIUS_PROBE_FACE_EVERY"),
        CultureInfo.InvariantCulture,
        out int every
    )
        ? every
        : 1;

    private static readonly StreamWriter? Log = Environment.GetEnvironmentVariable("TERTIUS_PROBE_FACE_LOG") is
    { Length: > 0 } path
        ? new StreamWriter(path) { AutoFlush = true }
        : null;

    private static readonly float LeastSine = MathF.Sin(MathF.Atan(1f / Size));
    private static long frame = -1;
    private static string line = "";

    /// <summary>Whether the size cull leaves out the sphere at <paramref name="center"/>, eye-relative.</summary>
    public static bool CullsSmall(System.Numerics.Vector3 center, float radius)
    {
        float distance = center.Length();
        return Cull && distance > radius && radius / distance < LeastSine;
    }

    /// <summary>The frame number that the in-turn rule uses, or −1 when this frame draws no in-turn face.</summary>
    public static long InTurnFrame(long drawn) => Every <= 1 ? drawn : drawn % Every == 0 ? drawn / Every : -1;

    /// <summary>Starts the record of drawn frame <paramref name="drawn"/> and writes the one before it.</summary>
    public static void Begin(long drawn)
    {
        if (Log is null)
            return;
        if (frame >= 0)
            Log.WriteLine(line);
        frame = drawn;
        line = drawn.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// With TERTIUS_PROBE_FACE_DUMP=dir, writes level 0 of the radiance cube of each of the first <paramref name="maps"/>
    /// maps as raw half floats, RGBA per texel, row by row: dir/map{m}-face{f}.f16, one file per face. Called once from
    /// the renderer's Dispose, before the device goes.
    /// </summary>
    public static void Dump(VulkanRenderer renderer, int maps)
    {
        if (Environment.GetEnvironmentVariable("TERTIUS_PROBE_FACE_DUMP") is not { Length: > 0 } dir)
            return;
        Directory.CreateDirectory(dir);
        for (int map = 0; map < maps; map++)
        for (int face = 0; face < CubeFaces.Count; face++)
        {
            Half[] texels = renderer.ReadEnvironmentFace(map, (CubeFace)face, 0, EnvironmentImage.Radiance);
            using var writer = new BinaryWriter(File.Create(Path.Combine(dir, $"map{map}-face{face}.f16")));
            foreach (Half texel in texels)
                writer.Write(texel);
        }
    }

    /// <summary>Adds one drawn face to the record of the current frame.</summary>
    public static void Face(int map, int face, int commands, long triangles, int kept, int culled)
    {
        if (Log is not null)
            line += FormattableString.Invariant($" {map}:{face}:{commands}:{triangles}:{kept}:{culled}");
    }
}
