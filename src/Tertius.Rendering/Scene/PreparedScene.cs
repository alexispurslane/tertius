using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>The interpolated camera of one frame. World space is camera-relative: the eye is at the origin.</summary>
internal struct CameraView
{
    /// <summary>The share of the shorter side of the screen over which a reflection fades out toward each edge.</summary>
    public const float EdgeFadeShare = 0.1f;

    /// <summary>The planes that bound the view: the near plane and the four side planes (<see cref="Bound"/>).</summary>
    private const int BoundCount = 5;

    public Quaternion Rotation;
    public Vector3 Right;
    public Vector3 Up;
    public Vector3 Forward;

    /// <summary>The interpolated eye position relative to the render origin. Subtracted from every position.</summary>
    public Vector3 Offset;

    public float VerticalFov;
    public float Aspect;

    /// <summary>
    /// The tangent of half the horizontal field of view. Setting it also sets the scale of the left and right planes of
    /// <see cref="Bound"/>, so that no test of a plane takes a square root.
    /// </summary>
    public float TanHalfX
    {
        readonly get;
        init
        {
            field = value;
            sideScaleX = SideScale(value);
        }
    }

    /// <summary>The tangent of half the vertical field of view. Setting it also sets the scale of the top and bottom planes.</summary>
    public float TanHalfY
    {
        readonly get;
        init
        {
            field = value;
            sideScaleY = SideScale(value);
        }
    }

    public int Width;
    public int Height;

    /// <summary>World to view rotation, in System.Numerics row-vector form.</summary>
    public Matrix4x4 View;

    /// <summary>The root frame's axes in world axes, interpolated like <see cref="Rotation"/>.</summary>
    public Quaternion RootRotation;

    /// <summary>SideScale of <see cref="TanHalfX"/>, set with it.</summary>
    private float sideScaleX;

    /// <summary>SideScale of <see cref="TanHalfY"/>, set with it.</summary>
    private float sideScaleY;

    /// <summary>The angle that one pixel covers at the center of the screen.</summary>
    public readonly float PixelAngle => 2f * TanHalfY / Math.Max(Height, 1);

    /// <summary>World to clip for one depth range, in System.Numerics row-vector form. See
    /// <see cref="DepthRanges.RangeProjection"/>.</summary>
    public readonly Matrix4x4 ViewProjection(int range) =>
        View * DepthRanges.RangeProjection(range, VerticalFov, Aspect);

    /// <summary>
    /// True when a sphere lies completely outside one of the planes that bound the view (<see cref="Bound"/>): behind the
    /// near plane or outside one of the four side planes. The planes are unrolled, so that each inlined Bound folds to its
    /// own plane: on the hot path of instance culling, a loop over the planes cost SceneBatcherBenchmarks 0.17 ms per
    /// 20,000 instances (ter-19fm).
    /// </summary>
    public readonly bool Culls(Vector3 center, float radius) =>
        Plane.DotCoordinate(Bound(0), center) + radius < 0
        || Plane.DotCoordinate(Bound(1), center) + radius < 0
        || Plane.DotCoordinate(Bound(2), center) + radius < 0
        || Plane.DotCoordinate(Bound(3), center) + radius < 0
        || Plane.DotCoordinate(Bound(4), center) + radius < 0;

    /// <summary>
    /// True when a spot lamp's cone at <paramref name="apex"/>, cut at <paramref name="reach"/>, lies completely outside one
    /// of the planes of <see cref="Culls(Vector3, float)"/>. Conservative: a cone outside the view near an edge where two
    /// planes meet, yet outside neither plane alone, is kept. A cone of a whole turn culls as the sphere of its reach.
    /// </summary>
    public readonly bool Culls(Vector3 apex, in Cone cone, float reach)
    {
        float sinOuter = MathF.Sin(cone.OuterAngle);
        for (int index = 0; index < BoundCount; index++)
        {
            Plane bound = Bound(index);
            if (Plane.DotCoordinate(bound, apex) + reach * Inward(bound.Normal, cone, sinOuter) < 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The distance in m from the eye to the point nearest the eye of the axis from <paramref name="apex"/> along the unit
    /// <paramref name="axis"/>, cut at <paramref name="reach"/> m, that lies inside the planes of
    /// <see cref="Culls(Vector3, float)"/>, or null when the cut axis misses the view.
    /// </summary>
    public readonly float? EyeDistanceToAxisInView(Vector3 apex, Vector3 axis, float reach)
    {
        float enter = 0;
        float leave = reach;
        for (int index = 0; index < BoundCount; index++)
        {
            Plane bound = Bound(index);
            // Inside the plane where inside + t · rate ≥ 0.
            float inside = Plane.DotCoordinate(bound, apex);
            float rate = Vector3.Dot(bound.Normal, axis);
            if (rate > 0)
                enter = MathF.Max(enter, -inside / rate);
            else if (rate < 0)
                leave = MathF.Min(leave, -inside / rate);
            else if (inside < 0)
                return null;
        }

        if (enter > leave)
            return null;
        // The squared distance to the eye is a parabola in t with its lowest point at −apex · axis.
        float along = Math.Clamp(-Vector3.Dot(apex, axis), enter, leave);
        return (apex + along * axis).Length();
    }

    /// <summary>
    /// A plane that bounds the view, with its unit normal pointing into the view: the near plane for
    /// <paramref name="index"/> 0, then the right, left, top and bottom side planes, all through the eye.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly Plane Bound(int index) =>
        index switch
        {
            0 => new(Forward, -DepthRanges.NearPlane),
            1 => new((TanHalfX * Forward - Right) * sideScaleX, 0),
            2 => new((TanHalfX * Forward + Right) * sideScaleX, 0),
            3 => new((TanHalfY * Forward - Up) * sideScaleY, 0),
            4 => new((TanHalfY * Forward + Up) * sideScaleY, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, "A view has five bounding planes."),
        };

    /// <summary>
    /// 1 / √(1 + tan²), which scales tan · Forward ± Right or tan · Forward ± Up, the inward normal of a side plane through
    /// the eye, of length √(1 + tan²), to unit length.
    /// </summary>
    private static float SideScale(float tan) => 1 / MathF.Sqrt(1 + tan * tan);

    /// <summary>
    /// How far a unit step along the directions of <paramref name="cone"/> goes along the unit <paramref name="normal"/>
    /// at most, and at least 0: 1 when the normal lies inside the cone, else the cosine of its angle to the cone's edge.
    /// </summary>
    private static float Inward(Vector3 normal, in Cone cone, float sinOuter)
    {
        float cosToAxis = Vector3.Dot(normal, cone.Axis);
        if (cosToAxis >= cone.CosOuter)
            return 1;
        // cos(φ − θ) = cos φ cos θ + sin φ sin θ, with φ the normal's angle to the axis and θ the outer half-angle.
        float sinToAxis = MathF.Sqrt(MathF.Max(0, 1 - cosToAxis * cosToAxis));
        return MathF.Max(0, cosToAxis * cone.CosOuter + sinToAxis * sinOuter);
    }

    /// <summary>
    /// The view ray through a position on the screen, in pixels from the top left. Its component along Forward is 1.
    /// The C# copy of viewRayThrough in screen.glsl (docs/plans/screen-space-reflections.md, "Terms").
    /// </summary>
    public readonly Vector3 ViewRayThrough(Vector2 pixel)
    {
        Vector2 ndc = pixel / new Vector2(Width, Height) * 2 - Vector2.One;
        return Forward + ndc.X * TanHalfX * Right - ndc.Y * TanHalfY * Up;
    }

    /// <summary>The position on the screen of a camera-relative point in front of the eye, in pixels from the top left.
    /// </summary>
    public readonly Vector2 PixelOf(Vector3 point)
    {
        float depth = Vector3.Dot(point, Forward);
        Vector2 ndc = new Vector2(Vector3.Dot(point, Right) / TanHalfX, -Vector3.Dot(point, Up) / TanHalfY) / depth;
        return (ndc + Vector2.One) * 0.5f * new Vector2(Width, Height);
    }

    /// <summary>1 over the view depth of a camera-relative point, in 1/m.</summary>
    public readonly float InverseDepthOf(Vector3 point) => 1 / Vector3.Dot(point, Forward);

    /// <summary>The edge fade at a position on the screen: 0 on the edges, 1 a band of EdgeFadeShare inside them.</summary>
    public readonly float EdgeFade(Vector2 pixel)
    {
        var size = new Vector2(Width, Height);
        var inside = Vector2.Clamp(
            Vector2.Min(pixel, size - pixel) / (EdgeFadeShare * Math.Min(Width, Height)),
            Vector2.Zero,
            Vector2.One
        );
        Vector2 smoothed = inside * inside * (new Vector2(3) - 2 * inside);
        return smoothed.X * smoothed.Y;
    }
}

/// <summary>
/// An instance after interpolation, before batching: a placed instance of the frame, or one of the main view's visible
/// instances, which also holds its depth.
/// </summary>
internal struct PreparedInstance
{
    /// <summary>Camera-relative position.</summary>
    public Vector3 Position;

    public Quaternion Rotation;
    public Vector3 Scale;
    public float Radius;

    /// <summary>View depth of the center: distance along the view axis.</summary>
    public float Depth;

    /// <summary>Distance of the center from the eye.</summary>
    public float Distance;

    public int Mesh;

    /// <summary>Index into the material buffer, already checked.</summary>
    public uint Material;

    public RenderFlags Flags;

    /// <summary>How the material's alpha draws: the <see cref="MaterialInfo"/> of <see cref="Material"/>'s slot.</summary>
    public AlphaMode Alpha;

    /// <summary>Whether the passes that shade draw the instance's back faces: the <see cref="MaterialInfo"/> of <see cref="Material"/>'s slot.</summary>
    public bool TwoSided;

    public uint EntityIndex;

    /// <summary>Which of the entity's render parts this is. With <see cref="EntityIndex"/>, it names the object from frame to frame.</summary>
    public ushort Part;

    /// <summary>Which way is up for the hemisphere ambient light.</summary>
    public HemisphereUp Hemisphere;

    /// <summary>The instance's map, <see cref="EnvironmentChain.MapOf"/> of its chain depth, or 0 with the map off.</summary>
    public int Map;

    /// <summary>Whether the instance's entity holds a light: a light of the snapshot carries the entity's index.</summary>
    public bool HoldsLight;

    /// <summary>
    /// The instance's place among the frame's placed instances: the number that tells its hair cards from another
    /// instance's, the same in every view and depth range that draws it (<see cref="CardCode.Of"/>).
    /// </summary>
    public int PlacedIndex;

    /// <summary>Whether the shadow views draw the instance: its flags hold <see cref="RenderFlags.CastsShadow"/>.</summary>
    public readonly bool CastsShadow => (Flags & RenderFlags.CastsShadow) != 0;
}

/// <summary>A run of indirect commands for one draw call.</summary>
internal readonly record struct CommandRange(int First, int Count);

/// <summary>
/// The opaque commands of one view: the solid draws, then the cutout draws (docs/plans/skin-and-hair.md, "The alpha modes
/// in the passes"). An opaque pass draws the solid run first, so that the early depth writes of its solid pipeline hide
/// the cutout fragments behind solid surfaces before their shader runs (decision 25).
/// </summary>
internal readonly record struct OpaqueRuns(CommandRange Solid, CommandRange Cutout)
{
    /// <summary>The commands of both runs.</summary>
    public int Count => Solid.Count + Cutout.Count;

    /// <summary>
    /// Whether an opaque pass draws an instance whose material draws as <paramref name="alpha"/> in the cutout run: a
    /// cutout or fringed material, whose texels below the cutoff the cutout pipeline discards.
    /// </summary>
    public static bool InCutoutRun(AlphaMode alpha) =>
        alpha switch
        {
            CutoutAlpha or FringedAlpha => true,
            OpaqueAlpha or GlassAlpha => false,
        };
}

/// <summary>
/// The opaque commands of one view of a pass that shades: the runs of the one-sided materials, whose back faces the pass
/// culls, and those of the two-sided ones, hair, whose back faces it draws (docs/plans/skin-and-hair.md, "Both sides of a
/// hair card"). The pass draws both solid runs before both cutout runs (decision 25). The shadow views cull no face, so
/// their commands are one <see cref="OpaqueRuns"/>.
/// </summary>
internal readonly record struct SidedRuns(OpaqueRuns OneSided, OpaqueRuns TwoSided)
{
    /// <summary>The commands of all four runs.</summary>
    public int Count => OneSided.Count + TwoSided.Count;

    /// <summary>The runs with the run that <paramref name="cutout"/> and <paramref name="twoSided"/> name set to <paramref name="commands"/>.</summary>
    public SidedRuns With(bool cutout, bool twoSided, CommandRange commands)
    {
        OpaqueRuns sided = twoSided ? TwoSided : OneSided;
        sided = cutout ? sided with { Cutout = commands } : sided with { Solid = commands };
        return twoSided ? this with { TwoSided = sided } : this with { OneSided = sided };
    }
}

/// <summary>The caster commands of a tile view that the frame draws: <see cref="PreparedScene.ShadowTiles"/>[View].</summary>
internal readonly record struct TileCommands(int View, OpaqueRuns Commands);

/// <summary>A run of GPU instances for one instanced draw.</summary>
internal readonly record struct InstanceRange(int First, int Count);

/// <summary>
/// A run of the transparent pass that draws glass: indirect commands, in order, of one-sided materials or of
/// <paramref name="TwoSided"/> ones, whose back faces it draws (<see cref="MaterialInfo.TwoSided"/>).
/// </summary>
internal readonly record struct GlassCommands(CommandRange Commands, bool TwoSided);

/// <summary>
/// A run of the transparent pass that draws the fringes of fringed instances, their texels below the cutoff blended by
/// their coverage: indirect commands, in order, of one-sided materials or of <paramref name="TwoSided"/> ones, whose back
/// faces it draws (<see cref="MaterialInfo.TwoSided"/>).
/// </summary>
internal readonly record struct FringeCommands(CommandRange Commands, bool TwoSided);

/// <summary>A run of the transparent pass that draws puffs: <see cref="PreparedScene.Puffs"/> from First, Count of them.</summary>
internal readonly record struct PuffSprites(int First, int Count);

/// <summary>
/// One stretch of the transparent pass's sorted list of one depth range that one draw covers: <see cref="GlassCommands"/>,
/// <see cref="FringeCommands"/> or <see cref="PuffSprites"/>. The empty value is the all-zero default, and it holds no case.
/// This is a generated union (docs/plans/union-tooling.md): the union generator writes its members.
/// </summary>
[GeneratedUnion(typeof(GlassCommands), typeof(FringeCommands), typeof(PuffSprites))]
internal readonly partial struct TransparentRun;

/// <summary>
/// Everything the CPU prepares for one frame: camera, the placed instances, visible instances, the sorted GPU instance
/// array, indirect commands per depth range and per shadow cascade, lights, occluders and the frame constants. Arrays are
/// reused from frame to frame.
/// </summary>
internal sealed class PreparedScene
{
    public CameraView Camera;

    /// <summary>The ambient light of the frame: the snapshot's hemisphere, or the map with its latest irradiance reading.</summary>
    public AmbientSource Ambient;

    /// <summary>
    /// The frame's atmosphere at the interpolated eye (<see cref="PaintedAtmosphere.Prepare"/>), set with the camera: the
    /// impostors' light signature reads it, and the frame constants carry it to the shaders.
    /// </summary>
    public GpuAtmosphere Atmosphere;

    public GpuFrameConstants Constants;
    public readonly Matrix4x4[] RangeViewProjection = new Matrix4x4[DepthRanges.Count];

    /// <summary>
    /// The placed instances of the frame: each instance of the snapshot that some view may draw, interpolated once, in the
    /// snapshot's order, with no depth.
    /// </summary>
    public PreparedInstance[] Placed = new PreparedInstance[256];

    public int PlacedCount;

    /// <summary>Visible instances. Instance processors may edit this list before batching.</summary>
    public PreparedInstance[] Visible = new PreparedInstance[256];

    public int VisibleCount;

    /// <summary>GPU instances in draw order. An opaque object in two depth ranges appears twice.</summary>
    public GpuInstance[] Instances = new GpuInstance[256];

    public int InstanceCount;

    public DrawIndexedIndirectCommand[] Commands = new DrawIndexedIndirectCommand[64];
    public int CommandCount;

    /// <summary>Opaque commands per depth range, indexed by <see cref="DepthRanges"/>: the solid runs, then the cutout runs.</summary>
    public readonly SidedRuns[] Opaque = new SidedRuns[DepthRanges.Count];

    /// <summary>
    /// The runs of the transparent pass per depth range, indexed by <see cref="DepthRanges"/>, each list in draw order. The
    /// transparent instances and the puffs of a range are sorted together back to front, and each stretch of one kind is one
    /// run. The pass draws the far range first.
    /// </summary>
    public readonly List<TransparentRun>[] Transparent =
    [
        [],
        [],
        [],
    ];

    /// <summary>The puffs to draw, in the order of the runs of <see cref="Transparent"/>.</summary>
    public GpuPuff[] Puffs = new GpuPuff[64];

    public int PuffCount;

    /// <summary>
    /// The lights of the frame, in the order of the snapshot: those that shine and can reach a view. Instance processors
    /// read them, for example to light impostor captures. <see cref="FrameResources.Write"/> writes their GPU form.
    /// </summary>
    public PreparedLight[] Lights = new PreparedLight[16];

    public int LightCount;

    /// <summary>
    /// The far shadows: xyz camera-relative center, w the radius of the caster's bounding sphere (see
    /// <see cref="Eclipse.Visibility"/>). Each far light with a slot has its own run of them, which its slot names.
    /// </summary>
    public Vector4[] Occluders = new Vector4[8];

    public int OccluderCount;

    /// <summary>
    /// The frame outlines to draw, in the order of the snapshot, appended by <see cref="OutlinePass"/> in its
    /// <see cref="RenderPass.Prepare"/>.
    /// </summary>
    public GpuOutline[] Outlines = new GpuOutline[16];

    public int OutlineCount;

    /// <summary>Instances in the snapshot, before any filtering.</summary>
    public int SubmittedInstances;

    /// <summary>The cascade sets of this frame, by set. Sets 0 to <see cref="CascadeSetCount"/> − 1 are valid.</summary>
    public readonly ShadowCascadeArray[] CascadeSets = new ShadowCascadeArray[ShadowCascades.MaxSets];

    /// <summary>The light basis of each cascade set, along its light's direction, by set.</summary>
    public readonly LightBasis[] CascadeBases = new LightBasis[ShadowCascades.MaxSets];

    /// <summary>
    /// The cascade sets that the shadow pass draws and the shading passes sample: one more than the highest set of a far
    /// light, or 0 when no light has a set or no caster comes within reach of the maps.
    /// </summary>
    public int CascadeSetCount;

    /// <summary>
    /// Caster commands per set and cascade, at set × <see cref="ShadowCascades.Count"/> + cascade, appended after batching
    /// by the shadow pass: the solid run, then the cutout run.
    /// </summary>
    public readonly OpaqueRuns[] ShadowCommands = new OpaqueRuns[ShadowCascades.MaxSets * ShadowCascades.Count];

    /// <summary>Distinct instances selected as shadow casters.</summary>
    public int ShadowCasters;

    /// <summary>Caster instances over all cascades: a caster in two cascades counts twice.</summary>
    public int ShadowInstances;

    /// <summary>
    /// The tile views of the frame, in the order of the lights' <see cref="AtlasTiles"/> slots, written by the local shadow
    /// pass. Views 0 to <see cref="ShadowTileCount"/> − 1 are valid.
    /// </summary>
    public GpuShadowTile[] ShadowTiles = new GpuShadowTile[16];

    public int ShadowTileCount;

    /// <summary>The tile views that the frame draws, because their records are stale, with their caster commands.</summary>
    public readonly List<TileCommands> DrawnTiles = [];

    /// <summary>Impostor billboards per depth range, appended after batching by the impostor pass.</summary>
    public readonly InstanceRange[] Impostors = new InstanceRange[DepthRanges.Count];

    /// <summary>The faces of the maps that this frame draws, map by map and each map's in layer order, with their opaque commands.</summary>
    public readonly List<EnvironmentFaceDraw> EnvironmentFaces = [];

    /// <summary>M, the served maps of this frame: 1 with the map off, whose one map every object takes.</summary>
    public int MapCount;

    /// <summary>R_i of the first <see cref="MapCount"/> maps, root first: the axes of map i's frame in world axes, interpolated.</summary>
    public readonly Quaternion[] MapAxes = new Quaternion[EnvironmentChain.MaxMaps];

    // The frame meshes, the jobs and their pose rows (docs/plans/skinning-and-morphs.md, "Posing in the batcher").
    private MeshInfo[] meshes = new MeshInfo[64];
    private int meshCount;
    private DeformJob[] deforms = new DeformJob[8];
    private int deformCount;
    private Vector4[] poses = new Vector4[64];
    private int poseCount;

    /// <summary>
    /// The frame meshes: the static meshes, then one deformed copy per job, in job order. Every instance, caster and
    /// command of the frame names its mesh in this list.
    /// </summary>
    public ReadOnlySpan<MeshInfo> Meshes => meshes.AsSpan(0, meshCount);

    /// <summary>How many of <see cref="Meshes"/> are static meshes, the meshes on the GPU as they were uploaded.</summary>
    public int StaticMeshCount { get; private set; }

    /// <summary>The frame's jobs, one per deformed copy, in the order of the snapshot's rig poses.</summary>
    public ReadOnlySpan<DeformJob> Deforms => deforms.AsSpan(0, deformCount);

    /// <summary>The pose rows of every job, job after job.</summary>
    public ReadOnlySpan<Vector4> Poses => poses.AsSpan(0, poseCount);

    /// <summary>The frame meshes as the passes that cache by mesh read them.</summary>
    public DeformedMeshes Deformed =>
        new(meshes: Meshes, staticMeshCount: StaticMeshCount, jobs: Deforms, poses: Poses);

    /// <summary>The posed instances that draw a deformed copy.</summary>
    public int DeformedInstances { get; private set; }

    /// <summary>The vertices of every deformed copy of the frame.</summary>
    public int DeformedVertices { get; private set; }

    /// <summary>The posed instances drawn as uploaded: a pose that does not fit, a full region, or a bound out of range.</summary>
    public int UndeformedPosedInstances { get; private set; }

    /// <summary>
    /// The transparent indirect commands over all ranges: the commands of every <see cref="GlassCommands"/> and
    /// <see cref="FringeCommands"/> run.
    /// </summary>
    public int TransparentCommandCount
    {
        get
        {
            int count = 0;
            foreach (List<TransparentRun> runs in Transparent)
            foreach (TransparentRun run in runs)
                count += run switch
                {
                    GlassCommands { Commands: var commands } => commands.Count,
                    FringeCommands { Commands: var commands } => commands.Count,
                    PuffSprites or null => 0,
                };

            return count;
        }
    }

    public Span<PreparedInstance> VisibleSpan => Visible.AsSpan(0, VisibleCount);

    public ReadOnlySpan<PreparedInstance> PlacedSpan => Placed.AsSpan(0, PlacedCount);

    /// <summary>Whether frame mesh <paramref name="mesh"/> is a deformed copy.</summary>
    public bool IsDeformed(int mesh) => mesh >= StaticMeshCount;

    public Span<PreparedLight> LightSpan => Lights.AsSpan(0, LightCount);

    /// <summary>
    /// Makes <paramref name="staticMeshes"/> the static part of <see cref="Meshes"/>, after <see cref="Reset"/> and before
    /// any job. Copies only the meshes uploaded since the call before, because a static mesh never changes once uploaded.
    /// So the static part is a cache of one table that only appends, GeometryStore.Meshes, and a scene serves one renderer.
    /// </summary>
    public void SetStaticMeshes(ReadOnlySpan<MeshInfo> staticMeshes)
    {
        Contract.Requires(staticMeshes.Length >= StaticMeshCount);
        EnsureRoom(ref meshes, staticMeshes.Length);
        staticMeshes[StaticMeshCount..].CopyTo(meshes.AsSpan(StaticMeshCount));
        StaticMeshCount = staticMeshes.Length;
        meshCount = StaticMeshCount;
    }

    /// <summary>The room for the next job's <paramref name="rows"/> pose rows, which <see cref="AddDeformed"/> then keeps.</summary>
    public Span<Vector4> NextPoseRows(int rows)
    {
        EnsureRoom(ref poses, poseCount + rows);
        return poses.AsSpan(poseCount, rows);
    }

    /// <summary>
    /// Appends a job of static mesh <paramref name="source"/>, which deforms by <paramref name="deform"/>, with the pose rows
    /// of <see cref="NextPoseRows"/>, and its frame mesh: the static mesh's indices over the copy's vertices in
    /// <paramref name="region"/>, of radius <paramref name="bound"/>. Returns the index of the frame mesh.
    /// </summary>
    public int AddDeformed(int source, MeshDeform deform, DeformRegion region, float bound)
    {
        // The copies lie end to end from the region's base, so the vertices of the copies before this one are its target.
        int target = DeformedVertices;
        EnsureRoom(ref deforms, deformCount + 1);
        deforms[deformCount++] = new(Mesh: source, Target: target, Pose: poseCount);
        poseCount += deform.PoseRows;
        EnsureRoom(ref meshes, meshCount + 1);
        meshes[meshCount] = meshes[source] with { VertexOffset = region.Base + target, Radius = bound, Deform = null };
        DeformedInstances++;
        DeformedVertices += deform.VertexCount;
        return meshCount++;
    }

    /// <summary>Counts a posed instance that draws as uploaded.</summary>
    public void CountUndeformed() => UndeformedPosedInstances++;

    /// <summary>Appends a visible instance, for example a billboard that replaces an object.</summary>
    public ref PreparedInstance AddVisible()
    {
        if (VisibleCount == Visible.Length)
            Array.Resize(ref Visible, Visible.Length * 2);
        ref PreparedInstance slot = ref Visible[VisibleCount++];
        slot = default;
        return ref slot;
    }

    /// <summary>Removes a visible instance by moving the last one into its place. Order is not kept.</summary>
    public void RemoveVisibleAt(int index)
    {
        if ((uint)index >= (uint)VisibleCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        Visible[index] = Visible[--VisibleCount];
    }

    /// <summary>Appends a light of the frame.</summary>
    public ref PreparedLight AddLight()
    {
        if (LightCount == Lights.Length)
            Array.Resize(ref Lights, Lights.Length * 2);
        ref PreparedLight slot = ref Lights[LightCount++];
        slot = default;
        return ref slot;
    }

    /// <summary>Appends a placed instance of the frame.</summary>
    public ref PreparedInstance AddPlaced()
    {
        if (PlacedCount == Placed.Length)
            Array.Resize(ref Placed, Placed.Length * 2);
        ref PreparedInstance slot = ref Placed[PlacedCount++];
        slot = default;
        return ref slot;
    }

    /// <summary>Appends a tile view of the frame.</summary>
    public void AddShadowTile(in GpuShadowTile tile)
    {
        if (ShadowTileCount == ShadowTiles.Length)
            Array.Resize(ref ShadowTiles, ShadowTiles.Length * 2);
        ShadowTiles[ShadowTileCount++] = tile;
    }

    /// <summary>Appends a far shadow, the sphere of a caster.</summary>
    public void AddOccluder(Vector4 sphere)
    {
        if (OccluderCount == Occluders.Length)
            Array.Resize(ref Occluders, Occluders.Length * 2);
        Occluders[OccluderCount++] = sphere;
    }

    /// <summary>Adds a GPU instance after batching, for a pass's own draws. Returns its index for firstInstance.</summary>
    public int AppendInstance(in GpuInstance instance)
    {
        if (InstanceCount == Instances.Length)
            Array.Resize(ref Instances, Instances.Length * 2);
        Instances[InstanceCount] = instance;
        return InstanceCount++;
    }

    /// <summary>Adds a frame outline to draw.</summary>
    public void AppendOutline(in GpuOutline outline)
    {
        if (OutlineCount == Outlines.Length)
            Array.Resize(ref Outlines, Outlines.Length * 2);
        Outlines[OutlineCount++] = outline;
    }

    /// <summary>Adds an indirect command after batching, for a pass's own draws. Returns its index.</summary>
    public int AppendCommand(in DrawIndexedIndirectCommand command)
    {
        if (CommandCount == Commands.Length)
            Array.Resize(ref Commands, Commands.Length * 2);
        Commands[CommandCount] = command;
        return CommandCount++;
    }

    public void Reset()
    {
        VisibleCount = 0;
        InstanceCount = 0;
        CommandCount = 0;
        LightCount = 0;
        PlacedCount = 0;
        OutlineCount = 0;
        OccluderCount = 0;
        SubmittedInstances = 0;
        Array.Clear(Opaque);
        foreach (List<TransparentRun> runs in Transparent)
            runs.Clear();
        PuffCount = 0;
        CascadeSetCount = 0;
        Array.Clear(ShadowCommands);
        ShadowCasters = 0;
        ShadowInstances = 0;
        ShadowTileCount = 0;
        DrawnTiles.Clear();
        Array.Clear(Impostors);
        EnvironmentFaces.Clear();
        meshCount = StaticMeshCount;
        deformCount = 0;
        poseCount = 0;
        DeformedInstances = 0;
        DeformedVertices = 0;
        UndeformedPosedInstances = 0;
    }

    private static void EnsureRoom<T>(ref T[] array, int count)
    {
        if (array.Length < count)
            Array.Resize(ref array, Math.Max(count, array.Length * 2));
    }
}

/// <summary>
/// Edits the visible instances after placement, culling, lights and occluders, and before batching, on the render
/// thread. <see cref="ImpostorManager"/> is one: it takes the objects it draws as impostors out of
/// <see cref="PreparedScene.Visible"/> and queues atlas captures, which <see cref="ImpostorPass"/> renders in
/// <see cref="FrameGraph.Stage.ImpostorCaptures"/> before the G-buffer pass. Register processors with
/// <see cref="SceneBatcher.Processors"/>.
/// </summary>
internal interface IInstanceProcessor
{
    void Process(PreparedScene scene);
}
