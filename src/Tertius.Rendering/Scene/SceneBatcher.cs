using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

/// <summary>A mesh as the batcher sees it: its range in the global buffers and its bounding radius.</summary>
internal readonly record struct MeshInfo(uint FirstIndex, uint IndexCount, int VertexOffset, float Radius);

/// <summary>
/// Turns a <see cref="RenderSnapshot"/> into GPU-ready arrays on the render thread: interpolates the camera, the
/// instances and the puffs, culls them against the frustum, assigns depth ranges, sorts, and builds one indirect command
/// per run of instances that share a depth range and a mesh. The transparent instances and the puffs sort together back
/// to front, and the batcher cuts that list into the runs of the transparent pass. Allocates nothing once its arrays have
/// grown to the scene.
/// </summary>
internal sealed class SceneBatcher
{
    private const float DefaultVerticalFov = MathF.PI / 3f;

    /// <summary>
    /// The smallest and the largest magnitude of a scale axis that the renderer draws: 2^−60 and 2^60. A zero axis
    /// collapses the object. The normal transform multiplies by 1 / scale², which stays a finite, normal single float
    /// for an axis in this range, and so does its square in the length of the transformed normal.
    /// </summary>
    private const float SmallestScale = 8.6736174e-19f,
        LargestScale = 1.1529215e18f;

    /// <summary>The smallest normal single float.</summary>
    private const float SmallestNormal = 1.17549435e-38f;

    private DrawItem[] opaque = new DrawItem[256];

    private DrawItem[] transparent = new DrawItem[64];

    // The far shadows of casters beyond the shadow maps, which follow those of casters in the maps.
    private Vector4[] beyond = new Vector4[8];

    // The puffs that this frame draws, in the order of the snapshot, before the sort puts them into PreparedScene.Puffs.
    private GpuPuff[] collectedPuffs = new GpuPuff[64];
    private int collectedPuffCount;

    /// <summary>Hooks that edit the visible instances before batching. See <see cref="IInstanceProcessor"/>.</summary>
    public List<IInstanceProcessor> Processors { get; } = [];

    /// <param name="snapshot">The tick to draw.</param>
    /// <param name="alpha">Interpolation factor from the previous transforms (0) to the current ones (1).</param>
    /// <param name="width">Output width in pixels.</param>
    /// <param name="height">Output height in pixels.</param>
    /// <param name="meshes">Meshes that are on the GPU. Instances of <see cref="MeshId.None"/> and of other meshes are skipped.</param>
    /// <param name="materialCount">
    /// Material slots on the GPU, the default included. <see cref="MaterialId.None"/> and ids that are not on the GPU
    /// draw with the default material (<see cref="MaterialStore.Resolve"/>).
    /// </param>
    /// <param name="scene">Receives the result.</param>
    public void Prepare(
        RenderSnapshot snapshot,
        float alpha,
        int width,
        int height,
        ReadOnlySpan<MeshInfo> meshes,
        int materialCount,
        PreparedScene scene
    )
    {
        alpha = ClampAlpha(alpha);
        scene.Reset();
        scene.Camera = InterpolateCamera(snapshot: snapshot, alpha: alpha, width: width, height: height);
        scene.Sun = snapshot.Sun;
        scene.Ambient = snapshot.Ambient;
        for (int range = 0; range < DepthRanges.Count; range++)
            scene.RangeViewProjection[range] = scene.Camera.ViewProjection(range);

        CollectVisible(snapshot: snapshot, alpha: alpha, meshes: meshes, materialCount: materialCount, scene: scene);
        CollectPuffs(snapshot, alpha, scene.Camera);
        // Before the processors: impostor captures are lit with the sun as the far shadows leave it.
        CollectLights(snapshot, scene);
        CollectFarShadows(snapshot: snapshot, alpha: alpha, meshes: meshes, scene: scene);
        foreach (IInstanceProcessor processor in Processors)
            processor.Process(scene);
        Batch(scene, meshes);
        WriteConstants(snapshot, scene);
    }

    private static CameraView InterpolateCamera(RenderSnapshot snapshot, float alpha, int width, int height)
    {
        RenderCamera camera = snapshot.Camera;
        Quaternion rotation = Interpolate(camera.PreviousRotation, camera.Rotation, alpha);
        float fov = camera.VerticalFov is > 0f and < MathF.PI ? camera.VerticalFov : DefaultVerticalFov;
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

    private static void CollectVisible(
        RenderSnapshot snapshot,
        float alpha,
        ReadOnlySpan<MeshInfo> meshes,
        int materialCount,
        PreparedScene scene
    )
    {
        ReadOnlySpan<RenderInstance> instances = CollectionsMarshal.AsSpan(snapshot.Instances);
        scene.SubmittedInstances = instances.Length;
        CameraView camera = scene.Camera;
        foreach (ref readonly RenderInstance instance in instances)
        {
            if ((instance.Flags & RenderFlags.Visible) == 0)
                continue;
            int mesh = MeshIndex(instance.Mesh, meshes.Length);
            if (mesh < 0)
                continue;
            Vector3 scale = instance.Scale;
            if (!HasVolume(scale))
                continue;

            Vector3 position = Vector3.Lerp(instance.PreviousPosition, instance.Position, alpha) - camera.Offset;
            float radius = BoundingRadius(instance, meshes[mesh]);
            // One sum is non-finite when any term is NaN or infinite. The mesh's extent bounds its scaled vertices, which
            // must stay finite when the bounding radius is set.
            if (
                !float.IsFinite(position.X + position.Y + position.Z + radius + MeshExtent(scale, meshes[mesh]))
                || camera.Culls(position, radius)
            )
                continue;

            ref PreparedInstance prepared = ref scene.AddVisible();
            prepared.Position = position;
            prepared.Rotation = Interpolate(instance.PreviousRotation, instance.Rotation, alpha);
            prepared.Scale = scale;
            prepared.Radius = radius;
            prepared.Depth = Vector3.Dot(position, camera.Forward);
            prepared.Distance = position.Length();
            prepared.Mesh = mesh;
            prepared.Material = MaterialStore.Resolve(instance.Material, materialCount);
            prepared.Flags = instance.Flags;
            prepared.EntityIndex = instance.EntityIndex;
            prepared.Part = instance.Part;
            prepared.Hemisphere = instance.Hemisphere;
        }
    }

    /// <summary>
    /// Keeps each puff that is alive at the render age, interpolated by alpha, as a <see cref="GpuPuff"/>. With its look L,
    /// the render age is its age less (1 − alpha) times <see cref="RenderSnapshot.PuffAgeStep"/>, and t is the render age
    /// over L's life: the radius is lerp(L.StartRadius, L.EndRadius, t), and the color is the own share 1 − t times the
    /// radiance or the albedo of L's shading. A puff is left out when its render age is below 0 or at its life or more,
    /// when its look is outside the snapshot's list or holds no shading, when its place or radius is not finite, when its
    /// radius is 0 or less, and when its sphere is outside the frustum.
    /// </summary>
    private void CollectPuffs(RenderSnapshot snapshot, float alpha, in CameraView camera)
    {
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
            // One sum is non-finite when any term is NaN or infinite. The fades of puff.frag divide by the radius, which a
            // GPU may flush to 0 below the smallest normal float.
            if (
                !float.IsFinite(position.X + position.Y + position.Z + radius)
                || !(radius >= SmallestNormal)
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
            collectedPuffs[collectedPuffCount++] = new() { PositionRadius = new(position, radius), ColorKind = color };
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
            if ((instance.Flags & RenderFlags.Transparent) != 0)
            {
                Append(
                    ref transparent,
                    ref transparentCount,
                    new(TransparentKey(instance.Depth, instance.Distance), i)
                );
                continue;
            }

            int mask = DepthRanges.OpaqueMask(instance.Depth, instance.Radius);
            for (int range = 0; range < DepthRanges.Count; range++)
                if ((mask & (1 << range)) != 0)
                    Append(ref opaque, ref opaqueCount, new(((ulong)range << 32) | (uint)instance.Mesh, i));
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

        opaque.AsSpan(0, opaqueCount).Sort(default(DrawItemComparer));
        transparent.AsSpan(0, transparentCount).Sort(default(DrawItemComparer));
        EnsureCapacity(ref scene.Instances, opaqueCount + transparentCount);
        Emit(scene: scene, meshes: meshes, items: opaque.AsSpan(0, opaqueCount), ranges: scene.Opaque);
        EmitTransparent(
            scene: scene,
            meshes: meshes,
            items: transparent.AsSpan(0, transparentCount),
            firstPuff: visible.Length
        );
    }

    /// <summary>
    /// Cuts the sorted transparent list into the runs of each range: a stretch of transparent instances becomes one
    /// <see cref="MeshCommands"/> run, with one indirect command per stretch of equal mesh, and a stretch of puffs becomes
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
        bool puffRun = false;
        foreach (DrawItem item in items)
        {
            int range = (int)(item.Key >> 32);
            bool isPuff = item.Index >= firstPuff;
            if (range != currentRange || isPuff != puffRun)
            {
                if (currentRange >= 0)
                    CloseRun(scene: scene, range: currentRange, puffRun: puffRun, runStart: runStart);
                currentRange = range;
                puffRun = isPuff;
                currentMesh = -1;
                runStart = isPuff ? scene.PuffCount : scene.CommandCount;
            }

            if (isPuff)
                scene.Puffs[scene.PuffCount++] = collectedPuffs[item.Index - firstPuff];
            else
                AppendInstanceCommand(
                    scene: scene,
                    meshes: meshes,
                    instance: ref scene.Visible[item.Index],
                    currentMesh: ref currentMesh
                );
        }

        if (currentRange >= 0)
            CloseRun(scene: scene, range: currentRange, puffRun: puffRun, runStart: runStart);
    }

    /// <summary>Adds the run of one kind that started at <paramref name="runStart"/> to the runs of its range.</summary>
    private static void CloseRun(PreparedScene scene, int range, bool puffRun, int runStart) =>
        scene
            .Transparent[range]
            .Add(
                puffRun
                    ? new PuffSprites(runStart, scene.PuffCount - runStart)
                    : new MeshCommands(new(runStart, scene.CommandCount - runStart))
            );

    /// <summary>
    /// Writes the GPU instance of <paramref name="instance"/> next in the instance array, and adds it to the last command
    /// when that command draws the same mesh (<paramref name="currentMesh"/>), or appends a command for it.
    /// </summary>
    private static void AppendInstanceCommand(
        PreparedScene scene,
        ReadOnlySpan<MeshInfo> meshes,
        ref PreparedInstance instance,
        ref int currentMesh
    )
    {
        int gpuIndex = scene.InstanceCount++;
        scene.Instances[gpuIndex] = GpuInstance.From(
            position: instance.Position,
            rotation: instance.Rotation,
            scale: instance.Scale,
            material: instance.Material,
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
    /// Writes the GPU instances in sorted order and one indirect command per run of equal (range, mesh), with
    /// firstInstance pointing at the run's first GPU instance.
    /// </summary>
    private static void Emit(
        PreparedScene scene,
        ReadOnlySpan<MeshInfo> meshes,
        ReadOnlySpan<DrawItem> items,
        CommandRange[] ranges
    )
    {
        int currentRange = -1,
            currentMesh = -1,
            rangeStart = scene.CommandCount;
        foreach (DrawItem item in items)
        {
            ref PreparedInstance instance = ref scene.Visible[item.Index];
            int range = (int)(item.Key >> 32);
            if (range != currentRange)
            {
                if (currentRange >= 0)
                    ranges[currentRange] = new(rangeStart, scene.CommandCount - rangeStart);
                currentRange = range;
                currentMesh = -1;
                rangeStart = scene.CommandCount;
            }

            AppendInstanceCommand(scene: scene, meshes: meshes, instance: ref instance, currentMesh: ref currentMesh);
        }

        if (currentRange >= 0)
            ranges[currentRange] = new(rangeStart, scene.CommandCount - rangeStart);
    }

    private static void CollectLights(RenderSnapshot snapshot, PreparedScene scene)
    {
        foreach (ref readonly RenderLight light in CollectionsMarshal.AsSpan(snapshot.Lights))
            // The default light holds no case, so it adds no light.
            switch (light)
            {
                case PointRenderLight point:
                    AppendPointLight(scene, point);
                    break;
                case SpotRenderLight spot:
                    AppendSpotLight(scene, spot);
                    break;
                case null:
                    break;
                default:
                    throw new UnreachableException($"The light {light} has a case that the batcher does not draw.");
            }
    }

    /// <summary>
    /// Adds a point light to the scene's lights, unless one of its numbers is not finite or it cannot reach a visible
    /// surface (see PositionInReach).
    /// </summary>
    private static void AppendPointLight(PreparedScene scene, in PointRenderLight light)
    {
        if (
            !IsFiniteLight(position: light.Position, color: light.Color, intensity: light.Intensity, range: light.Range)
            || PositionInReach(
                camera: scene.Camera,
                worldPosition: light.Position,
                intensity: light.Intensity,
                range: light.Range
            )
                is not { } position
        )
            return;
        // A point light has no direction and no cone, so those fields of its GpuLight stay zero. The shader and the
        // impostor lighting read them only for a spot light.
        Append(
            scene,
            new()
            {
                PositionRange = new(position, light.Range),
                RadianceKind = new(ShaderInput.Radiance(light.Color * light.Intensity), 0f),
            }
        );
    }

    /// <summary>
    /// Adds a spot light to the scene's lights, unless one of its numbers is not finite or it cannot reach a visible
    /// surface (see PositionInReach). Its axis goes at unit length, and its cone angles as the cosines of their values
    /// clamped to 0 to π.
    /// </summary>
    private static void AppendSpotLight(PreparedScene scene, in SpotRenderLight light)
    {
        if (
            !IsFiniteLight(position: light.Position, color: light.Color, intensity: light.Intensity, range: light.Range)
            || !ShaderInput.IsFinite(light.Direction)
            || !float.IsFinite(light.InnerConeAngle + light.OuterConeAngle)
            || PositionInReach(
                camera: scene.Camera,
                worldPosition: light.Position,
                intensity: light.Intensity,
                range: light.Range
            )
                is not { } position
        )
            return;
        float axisLengthSquared = light.Direction.LengthSquared();
        // The squared length of a huge axis overflows, and the axis cannot be normalized in single floats.
        Vector3 direction =
            float.IsFinite(axisLengthSquared) && axisLengthSquared > 1e-12f
                ? Vector3.Normalize(light.Direction)
                : -Vector3.UnitY;
        float cosOuter = MathF.Cos(Math.Clamp(light.OuterConeAngle, 0f, MathF.PI));
        float cosInner = MathF.Cos(Math.Clamp(light.InnerConeAngle, 0f, MathF.PI));
        // smoothstep needs a non-empty edge interval.
        cosInner = MathF.Max(cosInner, cosOuter + 1e-4f);
        Append(
            scene,
            new()
            {
                PositionRange = new(position, light.Range),
                RadianceKind = new(ShaderInput.Radiance(light.Color * light.Intensity), 1f),
                DirectionCosOuter = new(direction, cosOuter),
                Spot = new(x: cosInner, y: 0f, z: 0f, w: 0f),
            }
        );
    }

    /// <summary>Whether the numbers that both cases of a light hold are finite.</summary>
    private static bool IsFiniteLight(Vector3 position, Vector3 color, float intensity, float range) =>
        ShaderInput.IsFinite(position)
        && ShaderInput.IsFinite(color)
        && float.IsFinite(intensity)
        && float.IsFinite(range);

    /// <summary>
    /// The position relative to the camera, which is what the GPU light holds, of a light of this intensity and range at
    /// <paramref name="worldPosition"/> that can reach a visible surface: both are above zero, and the light's sphere of
    /// influence is not outside the frustum. Null for a light that cannot reach one.
    /// </summary>
    private static Vector3? PositionInReach(in CameraView camera, Vector3 worldPosition, float intensity, float range)
    {
        Vector3 position = worldPosition - camera.Offset;
        return !(range <= 0f || intensity <= 0f) && !camera.Culls(position, range) ? position : null;
    }

    private static void Append(PreparedScene scene, in GpuLight light)
    {
        if (scene.LightCount == scene.Lights.Length)
            Array.Resize(ref scene.Lights, scene.Lights.Length * 2);
        scene.Lights[scene.LightCount++] = light;
    }

    /// <summary>
    /// The far shadows: every shadow caster is also a sphere of its bounding radius, from any frame, visible or not, which
    /// shadows the sun for points outside it and never for points inside it (see <see cref="Eclipse.SunVisibility"/>). A
    /// caster that the shadow maps also draw (see <see cref="ShadowCascades.TakesCaster"/>) comes first in the list: its
    /// sphere counts only where the maps end. Only while the sun lights the scene, because a far shadow only blocks the sun.
    /// Only for a sphere that can be noticed: a caster beyond the maps must cover a pixel or a fifth of the sun's angular
    /// radius seen from the camera; a caster in the maps must hide 4% of the sun from where its far receivers start, more
    /// than 2 km from the camera. A smaller sphere hides at most 4% of the sun near the camera, and its shadow farther away
    /// is under a pixel too.
    /// </summary>
    private void CollectFarShadows(
        RenderSnapshot snapshot,
        float alpha,
        ReadOnlySpan<MeshInfo> meshes,
        PreparedScene scene
    )
    {
        if (!HasSun(snapshot.Sun))
            return;
        Vector3 offset = scene.Camera.Offset;
        // A sphere of radius r at distance d covers 2 asin(r / d), which is a pixel when r / d = sin(half a pixel). A sphere
        // r / d = a fifth of the sun's angular radius hides 4% of the sun.
        float sunShare = 0.2f * MathF.Sin(MathF.Max(snapshot.Sun.AngularRadius, 1e-5f));
        float smallestShare = MathF.Min(MathF.Sin(scene.Camera.PixelAngle * 0.5f), sunShare);
        int beyondCount = 0;
        foreach (ref readonly RenderInstance instance in CollectionsMarshal.AsSpan(snapshot.Instances))
        {
            if ((instance.Flags & RenderFlags.CastsShadow) == 0)
                continue;
            int mesh = MeshIndex(instance.Mesh, meshes.Length);
            if (mesh < 0 || !HasVolume(instance.Scale))
                continue;
            Vector3 position = Vector3.Lerp(instance.PreviousPosition, instance.Position, alpha) - offset;
            float radius = BoundingRadius(instance, meshes[mesh]);
            float distance = position.Length();
            if (!float.IsFinite(distance + radius))
                continue;
            if (ShadowCascades.TakesCaster(distance, radius))
            {
                // Its far receivers lie beyond the maps, at least 2 km from the camera, so at least this far from it.
                if (radius >= MathF.Max(ShadowCascades.Distance - distance, radius) * sunShare)
                    Append(ref scene.Occluders, ref scene.MappedOccluderCount, new(position, radius));
            }
            else if (radius >= distance * smallestShare)
            {
                Append(ref beyond, ref beyondCount, new(position, radius));
            }
        }

        scene.OccluderCount = scene.MappedOccluderCount;
        for (int i = 0; i < beyondCount; i++)
            Append(ref scene.Occluders, ref scene.OccluderCount, beyond[i]);
    }

    private static void Append(ref Vector4[] spheres, ref int count, Vector4 sphere)
    {
        if (count == spheres.Length)
            Array.Resize(ref spheres, spheres.Length * 2);
        spheres[count++] = sphere;
    }

    /// <summary>
    /// The sun lights the scene: a finite, non-zero direction and a finite, positive intensity. Every part of the frame
    /// asks this: without a sun, nothing is lit by it, no shadow map is drawn, and impostor captures bake no sunlight.
    /// </summary>
    internal static bool HasSun(in RenderSun sun)
    {
        float lengthSquared = sun.Direction.LengthSquared();
        // The threshold only tells a zero direction from a real one.
        bool hasDirection = float.IsFinite(lengthSquared) && lengthSquared > 1e-12f;
        return hasDirection && float.IsFinite(sun.Intensity) && sun.Intensity > 0f;
    }

    private static void WriteConstants(RenderSnapshot snapshot, PreparedScene scene)
    {
        CameraView camera = scene.Camera;
        RenderSun sun = snapshot.Sun;
        bool hasSun = HasSun(sun);
        AmbientLight ambient = snapshot.Ambient;
        scene.Constants = new()
        {
            FarViewProj = scene.RangeViewProjection[DepthRanges.Far],
            MiddleViewProj = scene.RangeViewProjection[DepthRanges.Middle],
            NearViewProj = scene.RangeViewProjection[DepthRanges.Near],
            CameraRight = new(camera.Right * camera.TanHalfX, 0f),
            CameraUp = new(camera.Up * camera.TanHalfY, 0f),
            CameraForward = new(camera.Forward, 0f),
            SunDirection = new(
                hasSun ? Vector3.Normalize(sun.Direction) : Vector3.UnitY,
                // A NaN fails the comparison: a point sun.
                sun.AngularRadius >= 0f
                    ? MathF.Min(sun.AngularRadius, MathF.PI)
                    : 0f
            ),
            SunRadiance = new(hasSun ? ShaderInput.Radiance(sun.Color * sun.Intensity) : Vector3.Zero, 0f),
            AmbientSky = new(ShaderInput.Radiance(ambient.SkyColor * ambient.Intensity), 0f),
            AmbientGround = new(ShaderInput.Radiance(ambient.GroundColor * ambient.Intensity), 0f),
            Viewport = new(
                x: camera.Width,
                y: camera.Height,
                z: 1f / Math.Max(camera.Width, 1),
                w: 1f / Math.Max(camera.Height, 1)
            ),
            Params = new(x: 0f, y: (float)snapshot.Time, z: camera.PixelAngle, w: 0f),
            LightCount = (uint)scene.LightCount,
            OccluderCount = (uint)scene.OccluderCount,
            // The shadow pass fills in the cascades when it draws them.
            ShadowCascadeCount = 0,
            MappedOccluderCount = (uint)scene.MappedOccluderCount,
            RootRotation = new(
                x: camera.RootRotation.X,
                y: camera.RootRotation.Y,
                z: camera.RootRotation.Z,
                w: camera.RootRotation.W
            ),
        };
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

    private readonly record struct DrawItem(ulong Key, int Index);

    /// <summary>Orders by key, then by visible index, so equal keys keep a deterministic order.</summary>
    private readonly struct DrawItemComparer : IComparer<DrawItem>
    {
        public int Compare(DrawItem a, DrawItem b) =>
            a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Index.CompareTo(b.Index);
    }
}
