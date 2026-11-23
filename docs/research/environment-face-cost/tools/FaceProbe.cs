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
/// <item>TERTIUS_PROBE_FACE_PARTS (t1-6tn2): parts of a face's work, joined by '+', each switched as follows.
/// The Earth is the instance of a mesh whose farthest vertex lies 6,000 to 7,000 km from its origin.
/// <list type="bullet">
/// <item>earth-off: the faces leave out the Earth.</item>
/// <item>earth-coarse: the faces draw the Earth as Primitives.Sphere(radius, 64, 32), 4,096 triangles, in place of
/// its own mesh. The main view keeps its own mesh.</item>
/// <item>earth-reach: a face draws an instance only in the depth ranges that its surface can reach. In a face's 90 degree
/// frustum a point at distance d has a view depth of at least d / sqrt(3), and every point of a sphere lies at least
/// |center| - radius from the eye, so a range whose far boundary lies below (|center| - radius) / sqrt(3) holds none
/// of it. Exact: the picture does not change. It applies to every instance.</item>
/// <item>own: a map's faces draw only the instances of the map's own frame (instance map = map), as a field point of
/// design P would (the lean field face). A texel that sees none of them stays empty.</item>
/// <item>specular-off: the prefilter skips the specular levels' dispatches of a map with fewer than six drawn faces.
/// The radiance mips, the irradiance convolution and its downsample still run.</item>
/// <item>irr-off: the prefilter skips the irradiance convolution's dispatch of a map with fewer than six drawn faces.
/// The irradiance downsample still runs.</item>
/// <item>prefilter-off: the prefilter pass records nothing in a frame that draws fewer than six faces per served map.
/// A refresh of all faces (the first draw) still prefilters, so the maps hold that draw's levels.</item>
/// </list></item>
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

    private static readonly string[] Parts = (Environment.GetEnvironmentVariable("TERTIUS_PROBE_FACE_PARTS") ?? "").Split(
        '+',
        StringSplitOptions.RemoveEmptyEntries
    );

    private static readonly bool EarthOff = Parts.Contains("earth-off");
    private static readonly bool EarthCoarse = Parts.Contains("earth-coarse");
    private static readonly bool EarthReach = Parts.Contains("earth-reach");
    private static readonly bool IrradianceOff = Parts.Contains("irr-off");
    private static readonly bool PrefilterOff = Parts.Contains("prefilter-off");
    private static readonly bool OwnOnly = Parts.Contains("own");
    private static readonly bool SpecularOff = Parts.Contains("specular-off");

    /// <summary>The Earth's mesh indices, each with its coarse mesh's index under earth-coarse (else itself).</summary>
    private static readonly Dictionary<int, int> Earths = [];

    private static PreparedInstance[] faceInstances = [];

    /// <summary>
    /// Called by VulkanRenderer.CreateMesh after it queued <paramref name="mesh"/> as <paramref name="id"/>: notes the
    /// Earth's mesh and, under earth-coarse, queues its coarse sphere through <paramref name="add"/>.
    /// </summary>
    public static void MeshCreated(Tertius.MeshId id, Tertius.MeshData mesh, Func<Tertius.MeshData, Tertius.MeshId> add)
    {
        float radius = 0f;
        foreach (Tertius.Vertex vertex in mesh.Vertices)
            radius = MathF.Max(radius, vertex.Position.Length());
        if (radius is < 6.0e6f or > 7.0e6f)
            return;
        Earths[id.Value] = EarthCoarse ? add(Tertius.Primitives.Sphere(radius, 64, 32)).Value : id.Value;
    }

    /// <summary>The instances that the faces draw: under earth-coarse, a copy whose Earth draws its coarse mesh.</summary>
    public static ReadOnlySpan<PreparedInstance> ForFaces(ReadOnlySpan<PreparedInstance> placed)
    {
        if (!EarthCoarse)
            return placed;
        if (faceInstances.Length < placed.Length)
            faceInstances = new PreparedInstance[placed.Length];
        placed.CopyTo(faceInstances);
        for (int i = 0; i < placed.Length; i++)
            if (Earths.TryGetValue(placed[i].Mesh, out int coarse))
                faceInstances[i].Mesh = coarse;
        return faceInstances.AsSpan(0, placed.Length);
    }

    /// <summary>
    /// Whether a face of map <paramref name="map"/> leaves out an instance of mesh <paramref name="mesh"/> in map
    /// <paramref name="objectMap"/>: the Earth under earth-off, another frame's instance under own.
    /// </summary>
    public static bool LeavesOut(int mesh, int objectMap, int map) =>
        (EarthOff && Earths.ContainsKey(mesh)) || (OwnOnly && objectMap != map);

    /// <summary>Under earth-reach, <paramref name="mask"/> less the depth ranges that the sphere's surface cannot reach.</summary>
    public static int Reach(int mask, System.Numerics.Vector3 center, float radius)
    {
        if (!EarthReach)
            return mask;
        float least = (center.Length() - radius) / MathF.Sqrt(3f);
        for (int range = 0; range < DepthRanges.Count; range++)
            if (DepthRanges.FarOf(range) < least)
                mask &= ~(1 << range);
        return mask;
    }

    /// <summary>Whether the prefilter skips a frame that draws <paramref name="faces"/> faces of <paramref name="maps"/> maps.</summary>
    public static bool SkipsPrefilter(int faces, int maps) => PrefilterOff && faces < CubeFaces.Count * maps;

    /// <summary>Whether the prefilter skips the specular levels of a map run of <paramref name="faces"/> faces.</summary>
    public static bool SkipsSpecular(uint faces) => SpecularOff && faces < CubeFaces.Count;

    /// <summary>Whether the prefilter skips the irradiance convolution of a map run of <paramref name="faces"/> faces.</summary>
    public static bool SkipsIrradiance(uint faces) => IrradianceOff && faces < CubeFaces.Count;

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
