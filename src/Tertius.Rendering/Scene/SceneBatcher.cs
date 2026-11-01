using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

/// <summary>A mesh as the batcher sees it: its range in the global buffers and its bounding radius.</summary>
/// <param name="Deform">The CPU side of a deforming mesh, or null for a rigid mesh and for a deformed copy.</param>
internal readonly record struct MeshInfo(
    uint FirstIndex,
    uint IndexCount,
    int VertexOffset,
    float Radius,
    MeshDeform? Deform
);

/// <summary>
/// Turns a <see cref="RenderSnapshot"/> into GPU-ready arrays on the render thread: interpolates the camera, the
/// instances, once for every view, the lights and the puffs, culls them, assigns depth ranges, sorts, and builds one
/// indirect command per stretch of instances that share a depth range, an opaque run and a mesh. It chooses the far
/// lights' shadow slots, fits their cascade sets, and gives them their far shadows (docs/plans/local-light-shadows.md).
/// The glass instances, the fringes and the puffs sort together back to front, and the batcher cuts that list into the
/// runs of the transparent pass. Allocates nothing once its arrays have grown to the scene.
/// </summary>
/// <param name="cascadedLights">How many far lights get a cascade set, 0 to <see cref="RendererOptions.MaxCascadedLights"/>.</param>
/// <param name="shadowMapSize">The side of each cascade's shadow map in texels, <see cref="ShadowMap.Size"/>.</param>
/// <param name="depthRangeScale">How many times deeper than wide each cascade box is, <see cref="ShadowMap.DepthRangeScale"/>.</param>
internal sealed class SceneBatcher(int cascadedLights, int shadowMapSize, float depthRangeScale)
{
    private const float DefaultVerticalFov = MathF.PI / 3f;

    /// <summary>
    /// The narrowest vertical field of view that the renderer draws, 10^−6 rad. The shaders normalize the camera's up and
    /// right axes, of length tan(fov / 2) and that times the aspect, whose squares stay normal single floats above it.
    /// </summary>
    private const float NarrowestVerticalFov = 1e-6f;

    /// <summary>
    /// The smallest and the largest magnitude of a scale axis that the renderer draws: 2^−60 and 2^60. A zero axis
    /// collapses the object. The normal transform multiplies by 1 / scale², which stays a finite, normal single float
    /// for an axis in this range, and so does its square in the length of the transformed normal.
    /// </summary>
    private const float SmallestScale = 8.6736174e-19f,
        LargestScale = 1.1529215e18f;

    /// <summary>
    /// The lowest of the two bits of an opaque draw item's key that name its run, so that the two read as one number:
    /// twice <see cref="CutoutKeyBit"/>'s plus <see cref="TwoSidedKeyBit"/>'s.
    /// </summary>
    private const int RunKeyShift = 30;

    /// <summary>
    /// The bit of an opaque draw item's key that puts it into its range's cutout runs, below the range's bits and just
    /// above <see cref="TwoSidedKeyBit"/>.
    /// </summary>
    internal const ulong CutoutKeyBit = TwoSidedKeyBit << 1;

    /// <summary>
    /// The bit of an opaque draw item's key that puts it into its range's two-sided runs, above the mesh's bits, which a
    /// mesh index below 2³⁰ never reaches: no geometry buffer holds a billion meshes.
    /// </summary>
    internal const ulong TwoSidedKeyBit = 1UL << RunKeyShift;

    /// <summary>The bits of an opaque draw item's key that name its run.</summary>
    private const ulong RunKeyBits = CutoutKeyBit | TwoSidedKeyBit;

    private DrawItem[] opaque = new DrawItem[256];
    private readonly OpaqueSorter opaqueSorter = new();

    // The frame mesh of each entry of the snapshot's rig poses, or −1 for an instance with no mesh on the GPU.
    private int[] entryMeshes = new int[16];

    private DrawItem[] transparent = new DrawItem[64];

    // The entities of this frame's snapshot that hold a light, which the lights carry. Refilled each frame.
    private readonly HashSet<uint> lightHolders = [];

    // The commands of one face per depth range, indexed by DepthRanges.
    private readonly SidedRuns[] faceRanges = new SidedRuns[DepthRanges.Count];

    // The puffs that this frame draws, in the order of the snapshot, before the sort puts them into PreparedScene.Puffs.
    private GpuPuff[] collectedPuffs = new GpuPuff[64];
    private int collectedPuffCount;

    /// <summary>Hooks that edit the visible instances before batching. See <see cref="IInstanceProcessor"/>.</summary>
    public List<IInstanceProcessor> Processors { get; } = [];

    /// <param name="snapshot">The tick to draw.</param>
    /// <param name="alpha">Interpolation factor from the previous transforms (0) to the current ones (1).</param>
    /// <param name="highestStops">
    /// The highest exposure, in stops, that the frame uses or its adapt step writes. Each light's reach ends at its cutoff.
    /// </param>
    /// <param name="width">Output width in pixels.</param>
    /// <param name="height">Output height in pixels.</param>
    /// <param name="meshes">Meshes that are on the GPU. Instances of <see cref="MeshId.None"/> and of other meshes are skipped.</param>
    /// <param name="materials">
    /// The material slots on the GPU, the default's first. <see cref="MaterialId.None"/> and ids that are not on the GPU
    /// draw with the default material (<see cref="MaterialStore.Resolve"/>).
    /// </param>
    /// <param name="environment">
    /// The faces that this frame draws of each served map, root first, and their latest irradiance, or no map. The served
    /// maps are those of <paramref name="snapshot"/>'s chain (docs/plans/environment-maps-per-frame.md).
    /// </param>
    /// <param name="region">The deform region that this frame's deformed copies fill.</param>
    /// <param name="scene">Receives the result.</param>
    public void Prepare(
        RenderSnapshot snapshot,
        float alpha,
        float highestStops,
        int width,
        int height,
        ReadOnlySpan<MeshInfo> meshes,
        ReadOnlySpan<MaterialInfo> materials,
        EnvironmentDraw environment,
        DeformRegion region,
        PreparedScene scene
    )
    {
        alpha = ClampAlpha(alpha);
        scene.Reset();
        scene.Camera = InterpolateCamera(snapshot: snapshot, alpha: alpha, width: width, height: height);
        scene.Atmosphere = PaintedAtmosphere.Prepare(snapshot: snapshot, alpha: alpha, eye: scene.Camera.Offset);
        // With the map off, one map stands for none, and every object takes map 0 (decision 24).
        (ReadOnlyMemory<FaceMask> drawn, scene.Ambient) = environment switch
        {
            WithMap { Faces: var faces, Irradiance: var readings } => (
                faces,
                new AmbientSource(new MapAmbient(readings))
            ),
            WithoutMap => (
                new ReadOnlyMemory<FaceMask>(NoFaces),
                new AmbientSource(new HemisphereAmbient(snapshot.Ambient))
            ),
        };
        ReadOnlySpan<FaceMask> facesOfMaps = drawn.Span;
        scene.MapCount = facesOfMaps.Length;
        InterpolateMapAxes(snapshot: snapshot, alpha: alpha, scene: scene);
        for (int range = 0; range < DepthRanges.Count; range++)
            scene.RangeViewProjection[range] = scene.Camera.ViewProjection(range);

        // First, because every collector reads the frame meshes and their bounds.
        PoseInstances(snapshot: snapshot, alpha: alpha, meshes: meshes, region: region, scene: scene);
        PlaceInstances(snapshot: snapshot, alpha: alpha, materials: materials, scene: scene);
        CollectPuffs(snapshot, alpha, scene);
        // Before the processors: impostor captures are lit by the lights as their far shadows leave them.
        CollectLights(
            snapshot: snapshot,
            alpha: alpha,
            cutoffIrradiance: LightUnits.CutoffIrradiance(highestStops),
            faces: facesOfMaps,
            scene: scene
        );
        // After the lights, which take the cascade sets, and before the far shadows, which follow what each set draws.
        FitCascadeSets(snapshot, scene);
        CollectFarShadows(scene);
        foreach (IInstanceProcessor processor in Processors)
            processor.Process(scene);
        Batch(scene, scene.Meshes);
        // After the main view, so that its instances and commands keep the places that they have without the map.
        BatchFaces(meshes: scene.Meshes, faces: facesOfMaps, scene: scene);
        WriteConstants(snapshot, scene);
    }

    /// <summary>
    /// The vertices that this frame's deformed copies can take: the sum of the vertex counts of the snapshot's posed
    /// instances that some view may draw and whose mesh deforms. The renderer grows the deform region to it before
    /// <see cref="Prepare"/>.
    /// </summary>
    public static int DeformNeed(RenderSnapshot snapshot, ReadOnlySpan<MeshInfo> meshes)
    {
        int need = 0;
        ReadOnlySpan<RenderInstance> instances = CollectionsMarshal.AsSpan(snapshot.Instances);
        foreach (ref readonly RenderRigPose pose in CollectionsMarshal.AsSpan(snapshot.RigPoses))
        {
            ref readonly RenderInstance instance = ref instances[pose.Instance];
            int mesh = MeshIndex(instance.Mesh, meshes.Length);
            if (mesh >= 0 && MayDraw(instance) && meshes[mesh].Deform is { } deform)
                need += deform.VertexCount;
        }

        return need;
    }

    /// <summary>The faces of the one map that stands for none while the map is off: it draws no face.</summary>
    private static readonly FaceMask[] NoFaces = [FaceMask.None];

    /// <summary>
    /// R_i of each served map, root first: the rotation of map i's frame, the chain frame at depth M − 1 − i, interpolated
    /// like the camera's (decision 9). A map with no frame on the chain, the one map of a snapshot without a chain, lies in
    /// world axes.
    /// </summary>
    private static void InterpolateMapAxes(RenderSnapshot snapshot, float alpha, PreparedScene scene)
    {
        ReadOnlySpan<RenderChainFrame> chain = CollectionsMarshal.AsSpan(snapshot.Chain);
        for (int map = 0; map < scene.MapCount; map++)
        {
            int depth = scene.MapCount - 1 - map;
            scene.MapAxes[map] =
                depth < chain.Length
                    ? Interpolate(chain[depth].PreviousRotation, chain[depth].Rotation, alpha)
                    : Quaternion.Identity;
        }
    }

    private static CameraView InterpolateCamera(RenderSnapshot snapshot, float alpha, int width, int height)
    {
        RenderCamera camera = snapshot.Camera;
        Quaternion rotation = Interpolate(camera.PreviousRotation, camera.Rotation, alpha);
        float fov = camera.VerticalFov is > 0f and < MathF.PI
            ? MathF.Max(camera.VerticalFov, NarrowestVerticalFov)
            : DefaultVerticalFov;
        float aspect = (float)Math.Max(width, 1) / Math.Max(height, 1);
        float tanY = MathF.Tan(fov * 0.5f);
        return new()
        {
            Rotation = rotation,
            Right = Vector3.Transform(Vector3.UnitX, rotation),
            Up = Vector3.Transform(Vector3.UnitY, rotation),
            Forward = Vector3.Transform(-Vector3.UnitZ, rotation),
            Offset = Vector3.Lerp(camera.PreviousPosition, camera.Position, alpha),
            VerticalFov = fov,
            Aspect = aspect,
            TanHalfY = tanY,
            TanHalfX = tanY * aspect,
            Width = width,
            Height = height,
            View = Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(rotation)),
            RootRotation = Interpolate(snapshot.PreviousRootRotation, snapshot.RootRotation, alpha),
        };
    }

    /// <summary>The interpolation factor as every part of the frame uses it: clamped to [0, 1], and 1 when not finite.</summary>
    internal static float ClampAlpha(float alpha) => float.IsFinite(alpha) ? Math.Clamp(alpha, 0f, 1f) : 1f;

    /// <summary>
    /// The world-space bounding radius of an instance: <see cref="RenderInstance.BoundingRadius"/> when it is set,
    /// else <see cref="MeshExtent"/>.
    /// </summary>
    internal static float BoundingRadius(in RenderInstance instance, in MeshInfo mesh) =>
        instance.BoundingRadius > 0f ? instance.BoundingRadius : MeshExtent(instance.Scale, mesh);

    /// <summary>How far the scaled mesh reaches from the instance's origin: the mesh radius times the largest scale factor.</summary>
    internal static float MeshExtent(Vector3 scale, in MeshInfo mesh) =>
        mesh.Radius * MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));

    /// <summary>
    /// The index of an instance's mesh in the meshes on the GPU, or −1 when it has none: <see cref="MeshId.None"/>, or
    /// an id that is not on the GPU, such as one from another renderer.
    /// </summary>
    internal static int MeshIndex(MeshId mesh, int meshCount) =>
        !mesh.IsNone && (uint)mesh.Value < (uint)meshCount ? mesh.Value : -1;

    /// <summary>Each axis of the scale is finite, and its magnitude lies from <see cref="SmallestScale"/> to <see cref="LargestScale"/>.</summary>
    internal static bool HasVolume(Vector3 scale) =>
        IsDrawableAxis(scale.X) && IsDrawableAxis(scale.Y) && IsDrawableAxis(scale.Z);

    /// <summary>
    /// Whether some view may draw the instance: it is visible or casts a shadow, and it has a volume. Every collector
    /// skips an instance without both.
    /// </summary>
    private static bool MayDraw(in RenderInstance instance)
    {
        bool shown = (instance.Flags & (RenderFlags.Visible | RenderFlags.CastsShadow)) != 0;
        return shown && HasVolume(instance.Scale);
    }

    // A NaN fails both comparisons.
    private static bool IsDrawableAxis(float axis) => MathF.Abs(axis) is >= SmallestScale and <= LargestScale;

    /// <summary>
    /// Slerp between two rotations. A zero quaternion (an unset field), or one whose squared length is not finite, counts
    /// as the other rotation, or identity.
    /// </summary>
    internal static Quaternion Interpolate(Quaternion previous, Quaternion current, float alpha)
    {
        bool hasCurrent = IsRotation(current);
        bool hasPrevious = IsRotation(previous);
        if (!hasCurrent)
            return hasPrevious ? Quaternion.Normalize(previous) : Quaternion.Identity;
        if (!hasPrevious || alpha >= 1f)
            return Quaternion.Normalize(current);
        return Quaternion.Normalize(
            Quaternion.Slerp(Quaternion.Normalize(previous), Quaternion.Normalize(current), alpha)
        );
    }

    // A NaN fails the comparison, and the squared length of a quaternion with an infinite or huge part is infinite.
    private static bool IsRotation(Quaternion rotation)
    {
        float lengthSquared = rotation.LengthSquared();
        return float.IsFinite(lengthSquared) && lengthSquared > 1e-12f;
    }

    /// <summary>
    /// Decides the frame mesh of each posed instance, in the snapshot's order (docs/plans/skinning-and-morphs.md, "Posing
    /// in the batcher"): a deformed copy when <see cref="DeformedCopy"/> makes one, else its static mesh, counted as drawn
    /// as uploaded. An instance whose mesh is not on the GPU, or that no view draws, is left to the collectors, which skip
    /// it, so that no job arises that no draw reads.
    /// </summary>
    /// <param name="meshes">The static meshes, which become the static part of the frame meshes.</param>
    private void PoseInstances(
        RenderSnapshot snapshot,
        float alpha,
        ReadOnlySpan<MeshInfo> meshes,
        DeformRegion region,
        PreparedScene scene
    )
    {
        scene.SetStaticMeshes(meshes);
        ReadOnlySpan<RenderRigPose> poses = CollectionsMarshal.AsSpan(snapshot.RigPoses);
        ReadOnlySpan<RenderInstance> instances = CollectionsMarshal.AsSpan(snapshot.Instances);
        EnsureCapacity(ref entryMeshes, poses.Length);
        for (int entry = 0; entry < poses.Length; entry++)
        {
            ref readonly RenderInstance instance = ref instances[poses[entry].Instance];
            int mesh = MeshIndex(instance.Mesh, meshes.Length);
            entryMeshes[entry] = mesh;
            if (mesh < 0 || !MayDraw(instance))
                continue;
            if (
                DeformedCopy(
                    snapshot: snapshot,
                    pose: poses[entry],
                    mesh: mesh,
                    alpha: alpha,
                    region: region,
                    scene: scene
                ) is
                { } copy
            )
                entryMeshes[entry] = copy;
            else
                scene.CountUndeformed();
        }
    }

    /// <summary>
    /// The frame mesh of a deformed copy of static mesh <paramref name="mesh"/> in <paramref name="pose"/>, appended with its
    /// job and its pose rows, or null when the instance draws as uploaded: the mesh does not deform, the rig pose does not
    /// fit it, the copy, its pose rows or its job do not fit in the region, or the posed bound is not finite or passes
    /// <see cref="ShaderInput.PositionLimit"/>.
    /// </summary>
    private static int? DeformedCopy(
        RenderSnapshot snapshot,
        in RenderRigPose pose,
        int mesh,
        float alpha,
        DeformRegion region,
        PreparedScene scene
    )
    {
        if (scene.Meshes[mesh].Deform is not { } deform || !deform.Fits(pose.JointCount, pose.WeightCount))
            return null;
        // The copies lie end to end, so the vertices of the copies so far are the next copy's target.
        bool fits = region.Holds(
            vertices: scene.DeformedVertices + deform.VertexCount,
            poseRows: scene.Poses.Length + deform.PoseRows,
            jobs: scene.Deforms.Length + 1
        );
        if (!fits)
            return null;
        float bound = deform.PoseInto(
            previousJoints: snapshot.PreviousJointsOf(pose),
            joints: snapshot.JointsOf(pose),
            previousWeights: snapshot.PreviousWeightsOf(pose),
            weights: snapshot.WeightsOf(pose),
            alpha: alpha,
            rows: scene.NextPoseRows(deform.PoseRows)
        );
        // A NaN fails the comparison. The rows of a dropped job stay unkept.
        if (!(bound <= ShaderInput.PositionLimit))
            return null;
        return scene.AddDeformed(source: mesh, deform: deform, region: region, bound: bound);
    }

    /// <summary>The frame meshes of the snapshot's posed instances, by entry of its rig poses, while they are walked.</summary>
    private FrameMeshCursor FrameMeshes(RenderSnapshot snapshot, PreparedScene scene) =>
        new(
            poses: CollectionsMarshal.AsSpan(snapshot.RigPoses),
            entryMeshes: entryMeshes,
            staticMeshCount: scene.StaticMeshCount
        );

    /// <summary>
    /// Places each instance that some view may draw once, with its frame mesh, into the scene's placed instances. The main
    /// view copies the visible ones that it does not cull, with their depth, into its visible instances. The faces and the
    /// shadow views select theirs (ARCHITECTURE.md, "One placement per instance").
    /// </summary>
    private void PlaceInstances(
        RenderSnapshot snapshot,
        float alpha,
        ReadOnlySpan<MaterialInfo> materials,
        PreparedScene scene
    )
    {
        ReadOnlySpan<RenderInstance> instances = CollectionsMarshal.AsSpan(snapshot.Instances);
        scene.SubmittedInstances = instances.Length;
        CameraView camera = scene.Camera;
        ReadOnlySpan<MeshInfo> meshes = scene.Meshes;
        FrameMeshCursor frameMeshes = FrameMeshes(snapshot, scene);
        CollectLightHolders(snapshot);
        for (int index = 0; index < instances.Length; index++)
        {
            ref readonly RenderInstance instance = ref instances[index];
            // Every instance moves the cursor, those that no view draws too.
            int mesh = frameMeshes.MeshOf(index, instance);
            if (
                Place(instance: instance, mesh: mesh, alpha: alpha, meshes: meshes, eye: camera.Offset)
                is not { } placement
            )
                continue;
            ref PreparedInstance placed = ref scene.AddPlaced();
            placed = Prepared(
                instance: instance,
                placement: placement,
                alpha: alpha,
                materials: materials,
                maps: scene.MapCount,
                holdsLight: lightHolders.Contains(instance.EntityIndex)
            ) with
            {
                PlacedIndex = scene.PlacedCount - 1,
            };
            bool visible = (instance.Flags & RenderFlags.Visible) != 0;
            if (visible && !camera.Culls(placement.Position, placement.Radius))
                scene.AddVisible() = placed with { Depth = Vector3.Dot(placement.Position, camera.Forward) };
        }
    }

    /// <summary>
    /// Where an instance draws, relative to the interpolated <paramref name="eye"/>, its bounding radius and its frame mesh
    /// <paramref name="mesh"/>. Null when it draws in no view: neither visible nor a caster, no mesh on the GPU (−1), no
    /// volume, or a place that reaches past <see cref="ShaderInput.PositionLimit"/> or is not finite.
    /// </summary>
    private static Placement? Place(
        in RenderInstance instance,
        int mesh,
        float alpha,
        ReadOnlySpan<MeshInfo> meshes,
        Vector3 eye
    )
    {
        if (mesh < 0 || !MayDraw(instance))
            return null;
        Vector3 position = Vector3.Lerp(instance.PreviousPosition, instance.Position, alpha) - eye;
        float radius = BoundingRadius(instance, meshes[mesh]);
        // The mesh's extent bounds its scaled vertices, which must stay in range also when the bounding radius is set.
        // MathF.Max passes a NaN on.
        return ShaderInput.InPositionRange(position, MathF.Max(radius, MeshExtent(instance.Scale, meshes[mesh])))
            ? new Placement(position, radius, mesh)
            : null;
    }

    /// <summary>
    /// The prepared instance of <paramref name="instance"/> at <paramref name="placement"/>, with no depth, which depends on
    /// the view, and its map of the <paramref name="maps"/> served maps.
    /// </summary>
    private static PreparedInstance Prepared(
        in RenderInstance instance,
        Placement placement,
        float alpha,
        ReadOnlySpan<MaterialInfo> materials,
        int maps,
        bool holdsLight
    )
    {
        uint material = MaterialStore.Resolve(instance.Material, materials.Length);
        return new()
        {
            Position = placement.Position,
            Rotation = Interpolate(instance.PreviousRotation, instance.Rotation, alpha),
            Scale = instance.Scale,
            Radius = placement.Radius,
            Distance = placement.Position.Length(),
            Mesh = placement.Mesh,
            Material = material,
            Alpha = materials[(int)material].Alpha,
            TwoSided = materials[(int)material].TwoSided,
            Flags = instance.Flags,
            EntityIndex = instance.EntityIndex,
            Part = instance.Part,
            Hemisphere = instance.Hemisphere,
            Map = EnvironmentChain.MapOf(instance.ChainDepth, maps),
            HoldsLight = holdsLight,
        };
    }

    /// <summary>
    /// Fills <see cref="lightHolders"/> with the entity of each light of <paramref name="snapshot"/>, whether it shines or
    /// not: an entity's glow is its light's face either way (docs/plans/screen-space-illumination.md, "The bounce source").
    /// </summary>
    private void CollectLightHolders(RenderSnapshot snapshot)
    {
        lightHolders.Clear();
        foreach (ref readonly RenderLight light in CollectionsMarshal.AsSpan(snapshot.Lights))
        {
            uint? holder = light switch
            {
                PointRenderLight point => point.EntityIndex,
                SpotRenderLight spot => spot.EntityIndex,
                // A light that holds no case is no light, and the renderer skips it.
                null => null,
            };
            if (holder is { } entity)
                lightHolders.Add(entity);
        }
    }

    /// <summary>
    /// Whether an instance whose material draws as <paramref name="alpha"/> draws in the transparent pass alone: glass. The
    /// shadow views still draw glass, from the placed instances that cast shadows (PreparedScene.Placed;
    /// docs/plans/skin-and-hair.md, "The alpha modes in the passes").
    /// </summary>
    private static bool IsGlass(AlphaMode alpha) =>
        alpha switch
        {
            GlassAlpha => true,
            OpaqueAlpha or CutoutAlpha or FringedAlpha => false,
        };

    /// <summary>
    /// Whether an instance whose material draws as <paramref name="alpha"/> draws in the transparent pass: glass, and the
    /// fringe of a fringed material, whose core also draws in the opaque passes.
    /// </summary>
    private static bool InTransparentPass(AlphaMode alpha) =>
        alpha switch
        {
            GlassAlpha or FringedAlpha => true,
            OpaqueAlpha or CutoutAlpha => false,
        };

    /// <summary>
    /// Prepares one view for each face that a map of <paramref name="faces"/> draws, map by map and each map's in layer
    /// order, after the main view's instances and commands: the opaque instances of the snapshot that the map draws (the
    /// draw rule) and that the face does not cull in the map's axes, by their depth along its axis, as geometry and with no
    /// impostor (docs/plans/environment-map.md, "Culling and level of detail", and docs/plans/environment-maps-per-frame.md,
    /// "What a map draws"). It reads them from the placed instances (<see cref="PlaceInstances"/>).
    /// </summary>
    /// <param name="faces">The faces of each served map, root first.</param>
    private void BatchFaces(ReadOnlySpan<MeshInfo> meshes, ReadOnlySpan<FaceMask> faces, PreparedScene scene)
    {
        if (!faces.ContainsAnyExcept(FaceMask.None))
            return;
        ReadOnlySpan<PreparedInstance> instances = scene.PlacedSpan;
        for (int map = 0; map < faces.Length; map++)
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        {
            var face = (CubeFace)layer;
            if ((faces[map] & CubeFaces.Of(face)) != 0)
                BatchFace(
                    meshes: meshes,
                    instances: instances,
                    map: map,
                    face: face,
                    basis: CubeFaces.Basis(face, scene.MapAxes[map]),
                    scene: scene
                );
        }
    }

    /// <summary>Prepares the view of one face of map <paramref name="map"/>, whose axes are <paramref name="basis"/>.</summary>
    private void BatchFace(
        ReadOnlySpan<MeshInfo> meshes,
        ReadOnlySpan<PreparedInstance> instances,
        int map,
        CubeFace face,
        FaceBasis basis,
        PreparedScene scene
    )
    {
        int opaqueCount = 0;
        for (int i = 0; i < instances.Length; i++)
        {
            ref readonly PreparedInstance instance = ref instances[i];
            if (
                !DrawsInMaps(instance)
                || !EnvironmentChain.Draws(map: map, objectMap: instance.Map)
                || CubeFaces.Culls(basis, instance.Position, instance.Radius)
            )
                continue;
            int mask = DepthRanges.OpaqueMask(Vector3.Dot(instance.Position, basis.Forward), instance.Radius);
            AppendOpaque(count: ref opaqueCount, mask: mask, instance: in instance, index: i);
        }

        opaqueSorter.Sort(opaque.AsSpan(0, opaqueCount), meshes.Length);
        EnsureCapacity(ref scene.Instances, scene.InstanceCount + opaqueCount);
        Array.Clear(faceRanges);
        Emit(
            scene: scene,
            meshes: meshes,
            instances: instances,
            items: opaque.AsSpan(0, opaqueCount),
            ranges: faceRanges
        );
        scene.EnvironmentFaces.Add(
            new(
                Map: map,
                Face: face,
                Basis: basis,
                Far: faceRanges[DepthRanges.Far],
                Middle: faceRanges[DepthRanges.Middle],
                Near: faceRanges[DepthRanges.Near]
            )
        );
    }

    /// <summary>
    /// Whether the maps draw a placed instance: it is visible and not glass. A fringed instance draws its core, as in every
    /// opaque pass (docs/plans/skin-and-hair.md, "The alpha modes in the passes").
    /// </summary>
    private static bool DrawsInMaps(in PreparedInstance instance) =>
        (instance.Flags & RenderFlags.Visible) != 0 && !IsGlass(instance.Alpha);

    /// <summary>
    /// Keeps each puff that is alive at the render age, interpolated by alpha, as a <see cref="GpuPuff"/>. With its look L,
    /// the render age is its age less (1 − alpha) times <see cref="RenderSnapshot.PuffAgeStep"/>, and t is the render age
    /// over L's life: the radius is lerp(L.StartRadius, L.EndRadius, t), and the color is the own share 1 − t times the
    /// radiance or the albedo of L's shading. A puff is left out when its render age is below 0 or at its life or more,
    /// when its look is outside the snapshot's list or holds no shading, when its sphere is not finite or reaches past
    /// <see cref="ShaderInput.PositionLimit"/>, when its radius is below the smallest normal float, and when its sphere
    /// is outside the frustum.
    /// </summary>
    private void CollectPuffs(RenderSnapshot snapshot, float alpha, PreparedScene scene)
    {
        ref readonly CameraView camera = ref scene.Camera;
        collectedPuffCount = 0;
        ReadOnlySpan<PuffLook> looks = CollectionsMarshal.AsSpan(snapshot.PuffLooks);
        float ageSinceRender = (1f - alpha) * snapshot.PuffAgeStep;
        foreach (ref readonly RenderPuff puff in CollectionsMarshal.AsSpan(snapshot.Puffs))
        {
            if ((uint)puff.Look >= (uint)looks.Length)
                continue;
            ref readonly PuffLook look = ref looks[puff.Look];
            float age = puff.Age - ageSinceRender;
            // A newborn is below 0 until the moment of its birth. A NaN fails both comparisons, and then the radius is a NaN.
            if (age < 0f || age >= look.Life)
                continue;
            float t = age / look.Life;
            float radius = look.StartRadius + (look.EndRadius - look.StartRadius) * t;
            Vector3 position = Vector3.Lerp(puff.PreviousPosition, puff.Position, alpha) - camera.Offset;
            // The fades of puff.frag divide by the radius, which a GPU may flush to 0 below the smallest normal float.
            if (
                !ShaderInput.InPositionRange(position, radius)
                || !(radius >= ShaderInput.SmallestNormal)
                || camera.Culls(position, radius)
            )
                continue;
            float ownShare = 1f - t;
            Vector4? colorKind = look.Shading switch
            {
                GlowPuffs glow => new Vector4(ShaderInput.Radiance(glow.Radiance * ownShare), GpuPuff.GlowKind),
                LitPuffs lit => new Vector4(ShaderInput.Fraction(lit.Albedo) * ownShare, GpuPuff.LitKind),
                null => null,
            };
            if (colorKind is not { } color)
                continue;
            if (collectedPuffCount == collectedPuffs.Length)
                Array.Resize(ref collectedPuffs, collectedPuffs.Length * 2);
            collectedPuffs[collectedPuffCount++] = new()
            {
                PositionRadius = new(position, radius),
                ColorKind = color,
                Map = (uint)EnvironmentChain.MapOf(puff.ChainDepth, scene.MapCount),
            };
        }
    }

    /// <summary>
    /// The sort key of a transparent instance or a puff: the far range first, and back to front inside a range. The
    /// inverted distance bits sort ascending.
    /// </summary>
    private static ulong TransparentKey(float depth, float distance) =>
        ((ulong)DepthRanges.Containing(depth) << 32) | ~BitConverter.SingleToUInt32Bits(MathF.Max(distance, 0f));

    private void Batch(PreparedScene scene, ReadOnlySpan<MeshInfo> meshes)
    {
        Span<PreparedInstance> visible = scene.VisibleSpan;
        int opaqueCount = 0,
            transparentCount = 0;
        for (int i = 0; i < visible.Length; i++)
        {
            ref PreparedInstance instance = ref visible[i];
            if ((uint)instance.Mesh >= (uint)meshes.Length)
                continue;
            if (InTransparentPass(instance.Alpha))
                Append(
                    ref transparent,
                    ref transparentCount,
                    new(TransparentKey(instance.Depth, instance.Distance), i)
                );
            if (!IsGlass(instance.Alpha))
                AppendOpaque(
                    count: ref opaqueCount,
                    mask: DepthRanges.OpaqueMask(instance.Depth, instance.Radius),
                    instance: in instance,
                    index: i
                );
        }

        // A puff's index follows those of the visible instances, so at equal keys an instance draws first, and puffs keep the
        // order of the snapshot.
        Vector3 forward = scene.Camera.Forward;
        for (int p = 0; p < collectedPuffCount; p++)
        {
            var center = new Vector3(
                collectedPuffs[p].PositionRadius.X,
                collectedPuffs[p].PositionRadius.Y,
                collectedPuffs[p].PositionRadius.Z
            );
            Append(
                ref transparent,
                ref transparentCount,
                new(TransparentKey(Vector3.Dot(center, forward), center.Length()), visible.Length + p)
            );
        }

        opaqueSorter.Sort(opaque.AsSpan(0, opaqueCount), meshes.Length);
        transparent.AsSpan(0, transparentCount).Sort(default(DrawItemComparer));
        EnsureCapacity(ref scene.Instances, opaqueCount + transparentCount);
        Emit(
            scene: scene,
            meshes: meshes,
            instances: visible,
            items: opaque.AsSpan(0, opaqueCount),
            ranges: scene.Opaque
        );
        EmitTransparent(
            scene: scene,
            meshes: meshes,
            items: transparent.AsSpan(0, transparentCount),
            firstPuff: visible.Length
        );
    }

    /// <summary>
    /// Adds one opaque draw item of <paramref name="instance"/> per depth range of <paramref name="mask"/>, keyed by the
    /// range, then the run, then the mesh. The runs go solid before cutout, and within each one-sided before two-sided, so
    /// that each range's commands form its solid runs and then its cutout runs.
    /// </summary>
    private void AppendOpaque(ref int count, int mask, in PreparedInstance instance, int index)
    {
        ulong run =
            (OpaqueRuns.InCutoutRun(instance.Alpha) ? CutoutKeyBit : 0) | (instance.TwoSided ? TwoSidedKeyBit : 0);
        for (int range = 0; range < DepthRanges.Count; range++)
            if ((mask & (1 << range)) != 0)
                Append(ref opaque, ref count, new(((ulong)range << 32) | run | (uint)instance.Mesh, index));
    }

    /// <summary>
    /// Cuts the sorted transparent list into the runs of each range: a stretch of glass or of fringes, of one-sided or of
    /// two-sided materials, becomes one <see cref="GlassCommands"/> or <see cref="FringeCommands"/> run, with one indirect
    /// command per stretch of equal mesh, and a stretch of puffs becomes
    /// one <see cref="PuffSprites"/> run of <see cref="PreparedScene.Puffs"/>. An item whose index is
    /// <paramref name="firstPuff"/> or more is the puff of that index less <paramref name="firstPuff"/>.
    /// </summary>
    private void EmitTransparent(
        PreparedScene scene,
        ReadOnlySpan<MeshInfo> meshes,
        ReadOnlySpan<DrawItem> items,
        int firstPuff
    )
    {
        EnsureCapacity(ref scene.Puffs, collectedPuffCount);
        int currentRange = -1,
            currentMesh = -1,
            runStart = 0;
        TransparentKind currentKind = TransparentKind.Glass;
        bool currentTwoSided = false;
        foreach (DrawItem item in items)
        {
            int range = (int)(item.Key >> 32);
            TransparentKind kind = KindOf(scene: scene, index: item.Index, firstPuff: firstPuff);
            bool twoSided = kind != TransparentKind.Puff && scene.Visible[item.Index].TwoSided;
            if (range != currentRange || kind != currentKind || twoSided != currentTwoSided)
            {
                if (currentRange >= 0)
                    CloseRun(
                        scene: scene,
                        range: currentRange,
                        kind: currentKind,
                        twoSided: currentTwoSided,
                        runStart: runStart
                    );
                currentRange = range;
                currentKind = kind;
                currentTwoSided = twoSided;
                currentMesh = -1;
                runStart = kind == TransparentKind.Puff ? scene.PuffCount : scene.CommandCount;
            }

            if (kind == TransparentKind.Puff)
                scene.Puffs[scene.PuffCount++] = collectedPuffs[item.Index - firstPuff];
            else
                AppendInstanceCommand(
                    scene: scene,
                    meshes: meshes,
                    instance: in scene.Visible[item.Index],
                    currentMesh: ref currentMesh
                );
        }

        if (currentRange >= 0)
            CloseRun(
                scene: scene,
                range: currentRange,
                kind: currentKind,
                twoSided: currentTwoSided,
                runStart: runStart
            );
    }

    /// <summary>
    /// What the transparent item of <paramref name="index"/> draws: the puff of that index less
    /// <paramref name="firstPuff"/>, or the fringe or the glass of that visible instance.
    /// </summary>
    private static TransparentKind KindOf(PreparedScene scene, int index, int firstPuff)
    {
        if (index >= firstPuff)
            return TransparentKind.Puff;
        return scene.Visible[index].Alpha switch
        {
            GlassAlpha => TransparentKind.Glass,
            FringedAlpha => TransparentKind.Fringe,
            OpaqueAlpha or CutoutAlpha => throw new UnreachableException(
                "An opaque or cutout material draws in no transparent pass."
            ),
        };
    }

    /// <summary>
    /// Adds the run of one kind, and of one-sided or <paramref name="twoSided"/> materials, that started at
    /// <paramref name="runStart"/> to the runs of its range.
    /// </summary>
    private static void CloseRun(PreparedScene scene, int range, TransparentKind kind, bool twoSided, int runStart)
    {
        var commands = new CommandRange(runStart, scene.CommandCount - runStart);
        scene
            .Transparent[range]
            .Add(
                kind switch
                {
                    TransparentKind.Glass => new GlassCommands(Commands: commands, TwoSided: twoSided),
                    TransparentKind.Fringe => new FringeCommands(Commands: commands, TwoSided: twoSided),
                    TransparentKind.Puff => new PuffSprites(runStart, scene.PuffCount - runStart),
                }
            );
    }

    /// <summary>
    /// Writes the GPU instance of <paramref name="instance"/> next in the instance array, and adds it to the last command
    /// when that command draws the same mesh (<paramref name="currentMesh"/>), or appends a command for it.
    /// </summary>
    private static void AppendInstanceCommand(
        PreparedScene scene,
        ReadOnlySpan<MeshInfo> meshes,
        in PreparedInstance instance,
        ref int currentMesh
    )
    {
        int gpuIndex = scene.InstanceCount++;
        scene.Instances[gpuIndex] = GpuInstance.From(
            position: instance.Position,
            rotation: instance.Rotation,
            scale: instance.Scale,
            material: instance.Material,
            map: instance.Map,
            holdsLight: instance.HoldsLight,
            placedIndex: instance.PlacedIndex,
            hemisphere: instance.Hemisphere
        );
        if (instance.Mesh == currentMesh)
        {
            scene.Commands[scene.CommandCount - 1].InstanceCount++;
            return;
        }

        currentMesh = instance.Mesh;
        MeshInfo mesh = meshes[instance.Mesh];
        if (scene.CommandCount == scene.Commands.Length)
            Array.Resize(ref scene.Commands, scene.Commands.Length * 2);
        scene.Commands[scene.CommandCount++] = new()
        {
            IndexCount = mesh.IndexCount,
            InstanceCount = 1,
            FirstIndex = mesh.FirstIndex,
            VertexOffset = mesh.VertexOffset,
            FirstInstance = (uint)gpuIndex,
        };
    }

    /// <summary>
    /// Writes the GPU instances in sorted order and one indirect command per run of equal (range, run, mesh), with
    /// firstInstance pointing at the run's first GPU instance, and each range's four runs into <paramref name="ranges"/>.
    /// An item's index is its place in <paramref name="instances"/>.
    /// </summary>
    private static void Emit(
        PreparedScene scene,
        ReadOnlySpan<MeshInfo> meshes,
        ReadOnlySpan<PreparedInstance> instances,
        ReadOnlySpan<DrawItem> items,
        SidedRuns[] ranges
    )
    {
        int currentRange = -1,
            currentMesh = -1,
            runStart = scene.CommandCount;
        ulong currentRun = 0;
        foreach (DrawItem item in items)
        {
            ref readonly PreparedInstance instance = ref instances[item.Index];
            int range = (int)(item.Key >> 32);
            ulong run = item.Key & RunKeyBits;
            if (range != currentRange || run != currentRun)
            {
                if (currentRange >= 0)
                    CloseOpaqueRun(
                        scene: scene,
                        ranges: ranges,
                        range: currentRange,
                        run: currentRun,
                        runStart: runStart
                    );
                currentRange = range;
                currentRun = run;
                currentMesh = -1;
                runStart = scene.CommandCount;
            }

            AppendInstanceCommand(scene: scene, meshes: meshes, instance: in instance, currentMesh: ref currentMesh);
        }

        if (currentRange >= 0)
            CloseOpaqueRun(scene: scene, ranges: ranges, range: currentRange, run: currentRun, runStart: runStart);
    }

    /// <summary>
    /// Sets the run of <paramref name="range"/> that the key bits <paramref name="run"/> name to the commands from
    /// <paramref name="runStart"/>.
    /// </summary>
    private static void CloseOpaqueRun(PreparedScene scene, SidedRuns[] ranges, int range, ulong run, int runStart)
    {
        var commands = new CommandRange(runStart, scene.CommandCount - runStart);
        ranges[range] = ranges[range]
            .With(cutout: (run & CutoutKeyBit) != 0, twoSided: (run & TwoSidedKeyBit) != 0, commands: commands);
    }

    /// <summary>
    /// Keeps each light of the snapshot, in its order, that <see cref="Shines(in RenderLight)"/> and whose reach can touch the
    /// main view or a face that keeps it: a face of a map at or above the light's map
    /// (docs/plans/environment-maps-per-frame.md, decision 13). One light list serves every view of the frame
    /// (docs/plans/environment-map.md, decision 20), each marked with whether the main view keeps it. Then chooses the far
    /// lights' slots (<see cref="FarShadows.Choose"/>).
    /// </summary>
    /// <param name="cutoffIrradiance">E_cut of the frame, in W/m², where each light's reach ends.</param>
    /// <param name="faces">The faces of each served map, root first.</param>
    private void CollectLights(
        RenderSnapshot snapshot,
        float alpha,
        double cutoffIrradiance,
        ReadOnlySpan<FaceMask> faces,
        PreparedScene scene
    )
    {
        ReadOnlySpan<RenderLight> lights = CollectionsMarshal.AsSpan(snapshot.Lights);
        // The atmosphere names its sun by its index in the snapshot's lights, and the scene by its place in the kept lights
        // (docs/plans/atmosphere.md, "The sun's tint").
        int? sun = snapshot.Atmosphere?.Sun;
        for (int i = 0; i < lights.Length; i++)
        {
            ref readonly RenderLight light = ref lights[i];
            if (!Shines(light))
                continue;
            PreparedLight prepared = Prepared(
                light: light,
                alpha: alpha,
                cutoffIrradiance: cutoffIrradiance,
                camera: scene.Camera,
                maps: scene.MapCount
            );
            if (!Reaches(faces: faces, axes: scene.MapAxes, light: prepared))
                continue;
            if (i == sun)
                scene.AirSunLight = scene.LightCount;
            scene.AddLight() = prepared;
        }

        FarShadows.Choose(scene.LightSpan, cascadedLights);
    }

    /// <summary>
    /// A light that <see cref="Shines(in RenderLight)"/>, interpolated by alpha, relative to the interpolated eye of
    /// <paramref name="camera"/>, with its radiant intensity, its reach to <paramref name="cutoffIrradiance"/> and its
    /// distance computed in doubles, whether the main view keeps it, its map among <paramref name="maps"/> served maps, and
    /// no shadow slot yet.
    /// </summary>
    private static PreparedLight Prepared(
        in RenderLight light,
        float alpha,
        double cutoffIrradiance,
        in CameraView camera,
        int maps
    )
    {
        (
            uint entity,
            Vector3 position,
            Vector3 color,
            double intensity,
            float radius,
            bool castsShadow,
            LightSpread spread,
            ushort chainDepth
        ) = light switch
        {
            PointRenderLight point => (
                point.EntityIndex,
                Vector3.Lerp(point.PreviousPosition, point.Position, alpha),
                point.Color,
                LightUnits.PointIntensity(point.Power),
                point.Radius,
                point.CastsShadow,
                new LightSpread(new EveryDirection()),
                point.ChainDepth
            ),
            SpotRenderLight spot => (
                spot.EntityIndex,
                Vector3.Lerp(spot.PreviousPosition, spot.Position, alpha),
                spot.Color,
                LightUnits.SpotIntensity(spot.Power, spot.OuterConeAngle),
                spot.Radius,
                spot.CastsShadow,
                new LightSpread(SpreadOf(spot, alpha)),
                spot.ChainDepth
            ),
            null => throw new UnreachableException("A light that shines holds a case."),
        };
        Vector3 relative = position - camera.Offset;
        float reach = (float)LightUnits.Reach(PeakChannel(color) * intensity, cutoffIrradiance);
        return new()
        {
            EntityIndex = entity,
            Position = relative,
            RadiantIntensity = LightUnits.RadiantIntensity(color, intensity),
            Radius = radius,
            Reach = reach,
            Distance = Math.Sqrt(
                (double)relative.X * relative.X + (double)relative.Y * relative.Y + (double)relative.Z * relative.Z
            ),
            CastsShadow = castsShadow,
            InMainView = !camera.Culls(relative, reach),
            Spread = spread,
            Map = EnvironmentChain.MapOf(chainDepth, maps),
        };
    }

    /// <summary>
    /// The cone of a spot light at alpha: its unit axes, a zero axis taken as straight down (−Y) and a zero previous axis
    /// as the current one, interpolated by <see cref="LightUnits.InterpolateAxis"/>, before <see cref="Cone.Of"/>, which
    /// needs a unit axis.
    /// </summary>
    private static Cone SpreadOf(in SpotRenderLight spot, float alpha)
    {
        Vector3 current = UnitOr(spot.Direction, -Vector3.UnitY);
        Vector3 previous = UnitOr(spot.PreviousDirection, current);
        return Cone.Of(
            axis: LightUnits.InterpolateAxis(previous, current, alpha),
            innerAngle: spot.InnerConeAngle,
            outerAngle: spot.OuterConeAngle
        );
    }

    /// <summary><paramref name="axis"/> at unit length, or <paramref name="fallback"/> for one too short or too long to normalize in single floats.</summary>
    private static Vector3 UnitOr(Vector3 axis, Vector3 fallback)
    {
        float lengthSquared = axis.LengthSquared();
        // The squared length of a huge axis overflows, and the threshold tells a zero axis from a real one.
        return float.IsFinite(lengthSquared) && lengthSquared > 1e-12f ? Vector3.Normalize(axis) : fallback;
    }

    /// <summary>The largest channel of a color, c_max, in doubles.</summary>
    private static double PeakChannel(Vector3 color) => Math.Max(color.X, Math.Max(color.Y, color.Z));

    /// <summary>
    /// Whether a light shines (docs/plans/local-light-shadows.md, decision 20): its places and color are finite, each color
    /// channel is 0 or more and one is above 0, its power is finite and above 0, its radius is finite and 0 or more, a spot
    /// light's axes and cone angles are finite, and c_max · I is at most <see cref="LightUnits.MaxIntensity"/>.
    /// </summary>
    internal static bool Shines(in RenderLight light) =>
        light switch
        {
            // The intensity is checked after the finite tests, so that it is a number.
            PointRenderLight point => Shines(
                place: point.Position,
                previousPlace: point.PreviousPosition,
                color: point.Color,
                power: point.Power,
                radius: point.Radius
            )
                && PeakChannel(point.Color) * LightUnits.PointIntensity(point.Power) <= LightUnits.MaxIntensity,
            SpotRenderLight spot => Shines(
                place: spot.Position,
                previousPlace: spot.PreviousPosition,
                color: spot.Color,
                power: spot.Power,
                radius: spot.Radius
            )
                && ShaderInput.IsFinite(spot.Direction)
                && ShaderInput.IsFinite(spot.PreviousDirection)
                && float.IsFinite(spot.InnerConeAngle)
                && float.IsFinite(spot.OuterConeAngle)
                && PeakChannel(spot.Color) * LightUnits.SpotIntensity(spot.Power, spot.OuterConeAngle)
                    <= LightUnits.MaxIntensity,
            null => false,
        };

    /// <summary>
    /// What every light needs to shine: finite places and color, each color channel 0 or more and one above 0, a finite
    /// power above 0, and a finite radius of 0 or more.
    /// </summary>
    private static bool Shines(Vector3 place, Vector3 previousPlace, Vector3 color, float power, float radius) =>
        ShaderInput.IsFinite(place)
        && ShaderInput.IsFinite(previousPlace)
        && ShaderInput.IsFinite(color)
        && color.X >= 0
        && color.Y >= 0
        && color.Z >= 0
        && PeakChannel(color) > 0
        && float.IsFinite(power)
        && power > 0
        && float.IsFinite(radius)
        && radius >= 0;

    /// <summary>
    /// Whether a prepared light can light a surface that a view draws: it lies within
    /// <see cref="ShaderInput.PositionLimit"/> of the eye, and the main view keeps it (<see cref="PreparedLight.InMainView"/>)
    /// or its reach sphere is not outside the frustum of a drawn face of a map that keeps the light: a map at or above
    /// the light's map, in the map's axes.
    /// </summary>
    private static bool Reaches(ReadOnlySpan<FaceMask> faces, Quaternion[] axes, in PreparedLight light) =>
        ShaderInput.InPositionRange(light.Position, 0f)
        && (
            light.InMainView
            || AnyFaceKeeps(faces: faces, lightMap: light.Map, axes: axes, center: light.Position, radius: light.Reach)
        );

    /// <summary>
    /// Whether a drawn face of a map at or above <paramref name="lightMap"/>, the maps that keep the light, keeps the sphere
    /// at <paramref name="center"/>, camera-relative, in its map's axes.
    /// </summary>
    private static bool AnyFaceKeeps(
        ReadOnlySpan<FaceMask> faces,
        int lightMap,
        Quaternion[] axes,
        Vector3 center,
        float radius
    )
    {
        for (int map = lightMap; map < faces.Length; map++)
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        {
            var face = (CubeFace)layer;
            if (
                (faces[map] & CubeFaces.Of(face)) != 0
                && !CubeFaces.Culls(CubeFaces.Basis(face, axes[map]), center, radius)
            )
                return true;
        }

        return false;
    }

    /// <summary>
    /// Fits the four cascades of each far light whose slot is a <see cref="CascadeSet"/> to the camera, along the light's
    /// own direction, with the texel grid through the origin of the camera's frame (<see cref="ShadowCascades.Anchor"/>).
    /// Leaves <see cref="PreparedScene.CascadeSetCount"/> at one more than the highest set, or 0 without one.
    /// </summary>
    private void FitCascadeSets(RenderSnapshot snapshot, PreparedScene scene)
    {
        // Worked out from this snapshot alone, so nothing carries over from tick to tick.
        Double3 anchor = ShadowCascades.Anchor(snapshot.OriginInFrame, scene.Camera.Offset);
        foreach (ref readonly PreparedLight light in scene.LightSpan)
        {
            if (light.Shadow is not CascadeSet { Set: var set })
                continue;
            // A far light is at least FarDistance from the eye, so its direction is not zero.
            var basis = LightBasis.FromDirection(light.Position);
            ShadowCascades.Fit(
                camera: scene.Camera,
                light: basis,
                mapSize: shadowMapSize,
                depthRangeScale: depthRangeScale,
                anchor: anchor,
                cascades: ref scene.CascadeSets[set]
            );
            scene.CascadeBases[set] = basis;
            scene.CascadeSetCount = Math.Max(scene.CascadeSetCount, set + 1);
        }
    }

    /// <summary>
    /// The far shadows of each far light with a slot, its own run of <see cref="PreparedScene.Occluders"/>: every caster
    /// is also a sphere of its bounding radius, which shadows the light for points outside it and never for points inside
    /// it (see <see cref="Eclipse.Visibility"/>). Only a sphere that can be noticed, by the light's own angular radius at
    /// the camera: a caster beyond the maps must cover a pixel or a fifth of that radius seen from the camera; a caster
    /// that the maps take must hide 4% of the light from 2 km from the camera, where the far receivers of a drawn caster
    /// start (<see cref="AddInReach"/>).
    /// The casters that the light's <see cref="CascadeSet"/> draws (<see cref="ShadowCascades.SetMask"/>) come first. For a
    /// <see cref="FarShadowsOnly"/> light every caster is beyond the maps.
    /// </summary>
    private static void CollectFarShadows(PreparedScene scene)
    {
        ReadOnlySpan<PreparedInstance> placed = scene.PlacedSpan;
        // A sphere of radius r at distance d covers 2 asin(r / d), which is a pixel when r / d = sin(half a pixel).
        float pixelShare = MathF.Sin(scene.Camera.PixelAngle * 0.5f);
        foreach (ref PreparedLight light in scene.LightSpan)
        {
            // A sphere r / d = a fifth of the light's angular radius hides 4% of the light.
            float lightShare =
                0.2f * MathF.Sin(MathF.Max(LightUnits.AngularRadius(light.Radius, (float)light.Distance), 1e-5f));
            float smallestShare = MathF.Min(pixelShare, lightShare);
            int start = scene.OccluderCount;
            switch (light.Shadow)
            {
                case CascadeSet set:
                    AddInReach(scene: scene, placed: placed, lightShare: lightShare, set: set.Set, drawn: true);
                    int mapped = scene.OccluderCount - start;
                    AddInReach(scene: scene, placed: placed, lightShare: lightShare, set: set.Set, drawn: false);
                    AddBeyond(scene: scene, placed: placed, smallestShare: smallestShare, mappedToo: false);
                    light.Shadow = new(
                        set with
                        {
                            Occluders = new(start, scene.OccluderCount - start),
                            Mapped = mapped,
                        }
                    );
                    break;
                case FarShadowsOnly:
                    AddBeyond(scene: scene, placed: placed, smallestShare: smallestShare, mappedToo: true);
                    light.Shadow = new(new FarShadowsOnly(new(start, scene.OccluderCount - start)));
                    break;
                case AtlasTiles or null:
                    break;
                default:
                    throw new UnreachableException();
            }
        }
    }

    /// <summary>
    /// The far shadows of the casters in reach of the maps (<see cref="ShadowCascades.InReach"/>) that hide 4% of the
    /// light from beyond the maps: those that cascade set <paramref name="set"/> draws, or those that it does not. A caster
    /// that the set does not draw counts as a sphere at receivers nearer than 2 km too, where this size test drops a
    /// shadow that can show; that is a known limit.
    /// </summary>
    private static void AddInReach(
        PreparedScene scene,
        ReadOnlySpan<PreparedInstance> placed,
        float lightShare,
        int set,
        bool drawn
    )
    {
        ref readonly LightBasis light = ref scene.CascadeBases[set];
        ref readonly ShadowCascadeArray cascades = ref scene.CascadeSets[set];
        foreach (ref readonly PreparedInstance caster in placed)
        {
            // The size test comes before the box test, which most casters in reach fail. The far receivers lie beyond
            // the maps, at least 2 km from the camera, so at least this far from the caster.
            if (
                !ShadowCascades.InReach(caster)
                || caster.Radius < MathF.Max(ShadowCascades.Distance - caster.Distance, caster.Radius) * lightShare
            )
                continue;
            bool drawnBySet = ShadowCascades.SetMask(light: light, cascades: cascades, caster: caster) != 0;
            if (drawnBySet == drawn)
                scene.AddOccluder(new(caster.Position, caster.Radius));
        }
    }

    /// <summary>
    /// The far shadows of the casters that cover a pixel or a fifth of the light's angular radius: those beyond the maps,
    /// or every caster when <paramref name="mappedToo"/>.
    /// </summary>
    private static void AddBeyond(
        PreparedScene scene,
        ReadOnlySpan<PreparedInstance> placed,
        float smallestShare,
        bool mappedToo
    )
    {
        foreach (ref readonly PreparedInstance caster in placed)
        {
            if (!caster.CastsShadow)
                continue;
            bool beyondMaps = mappedToo || !ShadowCascades.TakesCaster(caster.Distance, caster.Radius);
            if (beyondMaps && caster.Radius >= caster.Distance * smallestShare)
                scene.AddOccluder(new(caster.Position, caster.Radius));
        }
    }

    private static void WriteConstants(RenderSnapshot snapshot, PreparedScene scene)
    {
        CameraView camera = scene.Camera;
        // The frame constants hold the hemisphere only while it is the light (docs/plans/environment-map.md, "The
        // hemisphere and the lobe-sky table while the map is on").
        (Vector3 sky, Vector3 ground) = scene.Ambient switch
        {
            HemisphereAmbient(var light) => (light.SkyColor * light.Intensity, light.GroundColor * light.Intensity),
            MapAmbient => (Vector3.Zero, Vector3.Zero),
            null => throw new UnreachableException("Prepare sets the ambient source before it writes the constants."),
        };
        scene.Constants = new()
        {
            FarViewProj = scene.RangeViewProjection[DepthRanges.Far],
            MiddleViewProj = scene.RangeViewProjection[DepthRanges.Middle],
            NearViewProj = scene.RangeViewProjection[DepthRanges.Near],
            CameraRight = new(camera.Right * camera.TanHalfX, 0f),
            CameraUp = new(camera.Up * camera.TanHalfY, 0f),
            CameraForward = new(camera.Forward, 0f),
            AmbientSky = new(ShaderInput.Radiance(sky), 0f),
            AmbientGround = new(ShaderInput.Radiance(ground), 0f),
            Viewport = new(
                x: camera.Width,
                y: camera.Height,
                z: 1f / Math.Max(camera.Width, 1),
                w: 1f / Math.Max(camera.Height, 1)
            ),
            // Written after the batch, which sets the count of the puffs drawn.
            Params = new(
                x: ShaderInput.PuffRadianceCap(scene.PuffCount),
                y: (float)snapshot.Time,
                z: camera.PixelAngle,
                w: 0f
            ),
            // The shadow pass fills in the cascades when it draws them.
            Counts = new(
                X: (uint)scene.LightCount,
                Y: scene.AirSunLight is { } sun ? (uint)sun : GpuFrameConstants.NoAirSun,
                Z: 0,
                W: 0
            ),
            RootRotation = new(
                x: camera.RootRotation.X,
                y: camera.RootRotation.Y,
                z: camera.RootRotation.Z,
                w: camera.RootRotation.W
            ),
            Atmosphere = scene.Atmosphere,
        };
        for (int map = 0; map < scene.MapCount; map++)
        {
            Quaternion axes = scene.MapAxes[map];
            scene.Constants.MapAxes[map] = new(x: axes.X, y: axes.Y, z: axes.Z, w: axes.W);
        }
    }

    private static void Append(ref DrawItem[] items, ref int count, DrawItem item)
    {
        if (count == items.Length)
            Array.Resize(ref items, items.Length * 2);
        items[count++] = item;
    }

    private static void EnsureCapacity<T>(ref T[] array, int count)
    {
        if (array.Length < count)
            Array.Resize(ref array, Math.Max(count, array.Length * 2));
    }

    internal readonly record struct DrawItem(ulong Key, int Index);

    /// <summary>
    /// Sorts a view's opaque draw items by key, and keeps their order at equal keys. A view appends its items in the
    /// order of its instances, so this is the order of (key, index). A key is (range, run, mesh), one of
    /// <see cref="DepthRanges.Count"/> × 4 × meshes, so the sorter counts the items of each key and moves each item once: a
    /// counting sort, in time linear in the items and the meshes, where a comparison sort takes n log n comparisons.
    /// Allocates nothing once its arrays have grown to the view.
    /// </summary>
    internal sealed class OpaqueSorter
    {
        /// <summary>The runs of a range: solid and cutout, each one-sided and two-sided.</summary>
        private const int RunsPerRange = 4;

        private int[] starts = new int[64];
        private DrawItem[] sorted = new DrawItem[256];

        /// <summary>Sorts <paramref name="items"/> of meshes 0 to <paramref name="meshCount"/> − 1.</summary>
        public void Sort(Span<DrawItem> items, int meshCount)
        {
            // The four runs of each range.
            int keys = DepthRanges.Count * RunsPerRange * meshCount;
            EnsureCapacity(ref starts, keys + 1);
            EnsureCapacity(ref sorted, items.Length);
            Span<int> start = starts.AsSpan(0, keys + 1);
            start.Clear();
            foreach (DrawItem item in items)
                start[KeyIndex(item, meshCount) + 1]++;
            for (int key = 1; key < start.Length; key++)
                start[key] += start[key - 1];
            foreach (DrawItem item in items)
                sorted[start[KeyIndex(item, meshCount)]++] = item;
            sorted.AsSpan(0, items.Length).CopyTo(items);
        }

        /// <summary>
        /// The place of an item's key in key order: its range, then its run, solid before cutout and one-sided before
        /// two-sided, then its mesh.
        /// </summary>
        private static int KeyIndex(DrawItem item, int meshCount)
        {
            // The two run bits read as one number: twice the cutout bit plus the two-sided bit.
            int run = (int)((item.Key & RunKeyBits) >> RunKeyShift);
            int mesh = (int)((uint)item.Key & ~(uint)RunKeyBits);
            return ((int)(item.Key >> 32) * RunsPerRange + run) * meshCount + mesh;
        }
    }

    /// <summary>
    /// Gives each instance of the snapshot its frame mesh, walked in instance order beside the rig poses, which are sorted by
    /// instance: the frame mesh of its entry for a posed instance, else its static mesh, or −1 for no mesh on the GPU.
    /// </summary>
    private ref struct FrameMeshCursor
    {
        private readonly ReadOnlySpan<RenderRigPose> poses;
        private readonly ReadOnlySpan<int> entryMeshes;
        private readonly int staticMeshCount;
        private int next;

        public FrameMeshCursor(ReadOnlySpan<RenderRigPose> poses, ReadOnlySpan<int> entryMeshes, int staticMeshCount)
        {
            this.poses = poses;
            this.entryMeshes = entryMeshes;
            this.staticMeshCount = staticMeshCount;
        }

        /// <summary>The frame mesh of instance <paramref name="index"/>, after every instance before it.</summary>
        public int MeshOf(int index, in RenderInstance instance)
        {
            if (next < poses.Length && poses[next].Instance == index)
                return entryMeshes[next++];
            // Against the static meshes only, so that an id never names a deformed copy.
            return MeshIndex(instance.Mesh, staticMeshCount);
        }
    }

    /// <summary>What a run of the transparent pass draws. No case carries a field, so it is an enum.</summary>
    private enum TransparentKind
    {
        Glass,
        Fringe,
        Puff,
    }

    /// <summary>Where an instance draws relative to the eye, its bounding radius, and the index of its mesh on the GPU.</summary>
    private readonly record struct Placement(Vector3 Position, float Radius, int Mesh);

    /// <summary>Orders by key, then by visible index, so equal keys keep a deterministic order.</summary>
    private readonly struct DrawItemComparer : IComparer<DrawItem>
    {
        public int Compare(DrawItem a, DrawItem b) =>
            a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Index.CompareTo(b.Index);
    }
}
