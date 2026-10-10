using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>A mesh as the batcher sees it: its range in the global buffers and its bounding radius.</summary>
internal readonly record struct MeshInfo(uint FirstIndex, uint IndexCount, int VertexOffset, float Radius);

/// <summary>
/// Turns a <see cref="RenderSnapshot"/> into GPU-ready arrays on the render thread: interpolates the camera and the
/// instances, culls them against the frustum, assigns depth ranges, sorts, and builds one indirect command per run
/// of instances that share a depth range and a mesh. Allocates nothing once its arrays have grown to the scene.
/// </summary>
internal sealed class SceneBatcher
{
    private const float DefaultVerticalFov = MathF.PI / 3f;

    private DrawItem[] opaque = new DrawItem[256];
    private DrawItem[] transparent = new DrawItem[64];

    /// <summary>Hooks that edit the visible instances before batching. See <see cref="IInstanceProcessor"/>.</summary>
    public List<IInstanceProcessor> Processors { get; } = new();

    /// <param name="snapshot">The tick to draw.</param>
    /// <param name="alpha">Interpolation factor from the previous transforms (0) to the current ones (1).</param>
    /// <param name="width">Output width in pixels.</param>
    /// <param name="height">Output height in pixels.</param>
    /// <param name="meshes">Meshes that are on the GPU. Instances of other meshes are skipped.</param>
    /// <param name="materialCount">Materials on the GPU. Other material ids fall back to the default material 0.</param>
    /// <param name="scene">Receives the result.</param>
    public void Prepare(RenderSnapshot snapshot, float alpha, int width, int height, ReadOnlySpan<MeshInfo> meshes, int materialCount, PreparedScene scene)
    {
        alpha = ClampAlpha(alpha);
        scene.Reset();
        scene.Camera = InterpolateCamera(snapshot.Camera, alpha, width, height);
        scene.Sun = snapshot.Sun;
        scene.Ambient = snapshot.Ambient;
        for (int range = 0; range < DepthRanges.Count; range++)
            scene.RangeViewProjection[range] = scene.Camera.ViewProjection(range);

        CollectVisible(snapshot, alpha, meshes, materialCount, scene);
        // Before the processors: impostor captures are lit with the sun as the occluders leave it.
        CollectLights(snapshot, scene);
        foreach (IInstanceProcessor processor in Processors)
            processor.Process(scene);
        Batch(scene, meshes);
        WriteConstants(snapshot, scene);
    }

    private static CameraView InterpolateCamera(in RenderCamera camera, float alpha, int width, int height)
    {
        Quaternion rotation = Interpolate(camera.PreviousRotation, camera.Rotation, alpha);
        float fov = camera.VerticalFov > 0f && camera.VerticalFov < MathF.PI ? camera.VerticalFov : DefaultVerticalFov;
        float aspect = (float)Math.Max(width, 1) / Math.Max(height, 1);
        float tanY = MathF.Tan(fov * 0.5f);
        return new CameraView
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
        };
    }

    /// <summary>The interpolation factor as every part of the frame uses it: clamped to [0, 1], and 1 when not finite.</summary>
    internal static float ClampAlpha(float alpha) => float.IsFinite(alpha) ? Math.Clamp(alpha, 0f, 1f) : 1f;

    /// <summary>
    /// The world-space bounding radius of an instance: <see cref="RenderInstance.BoundingRadius"/> when it is set,
    /// else the mesh radius times the largest scale factor.
    /// </summary>
    internal static float BoundingRadius(in RenderInstance instance, in MeshInfo mesh)
    {
        Vector3 scale = instance.Scale;
        return instance.BoundingRadius > 0f
            ? instance.BoundingRadius
            : mesh.Radius * MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
    }

    /// <summary>A zero scale axis collapses the object, and the normal matrix would divide by zero.</summary>
    internal static bool HasVolume(Vector3 scale) => scale.X != 0f && scale.Y != 0f && scale.Z != 0f;

    /// <summary>Slerp between two rotations. A zero quaternion (an unset field) counts as the other rotation, or identity.</summary>
    internal static Quaternion Interpolate(Quaternion previous, Quaternion current, float alpha)
    {
        bool hasCurrent = current.LengthSquared() > 1e-12f;
        bool hasPrevious = previous.LengthSquared() > 1e-12f;
        if (!hasCurrent)
            return hasPrevious ? Quaternion.Normalize(previous) : Quaternion.Identity;
        if (!hasPrevious || alpha >= 1f)
            return Quaternion.Normalize(current);
        return Quaternion.Normalize(Quaternion.Slerp(Quaternion.Normalize(previous), Quaternion.Normalize(current), alpha));
    }

    private static void CollectVisible(RenderSnapshot snapshot, float alpha, ReadOnlySpan<MeshInfo> meshes, int materialCount, PreparedScene scene)
    {
        ReadOnlySpan<RenderInstance> instances = CollectionsMarshal.AsSpan(snapshot.Instances);
        scene.SubmittedInstances = instances.Length;
        CameraView camera = scene.Camera;
        foreach (ref readonly RenderInstance instance in instances)
        {
            if ((instance.Flags & RenderFlags.Visible) == 0)
                continue;
            int mesh = instance.Mesh.Value;
            if ((uint)mesh >= (uint)meshes.Length)
                continue;
            Vector3 scale = instance.Scale;
            if (!HasVolume(scale))
                continue;

            Vector3 position = Vector3.Lerp(instance.PreviousPosition, instance.Position, alpha) - camera.Offset;
            float radius = BoundingRadius(instance, meshes[mesh]);
            if (!float.IsFinite(position.X + position.Y + position.Z + radius) || camera.Culls(position, radius))
                continue;

            ref PreparedInstance prepared = ref scene.AddVisible();
            prepared.Position = position;
            prepared.Rotation = Interpolate(instance.PreviousRotation, instance.Rotation, alpha);
            prepared.Scale = scale;
            prepared.Radius = radius;
            prepared.Depth = Vector3.Dot(position, camera.Forward);
            prepared.Distance = position.Length();
            prepared.Mesh = mesh;
            int material = instance.Material.Value;
            prepared.Material = (uint)material < (uint)materialCount ? (uint)material : 0u;
            prepared.Flags = instance.Flags;
            prepared.EntityIndex = instance.EntityIndex;
        }
    }

    private void Batch(PreparedScene scene, ReadOnlySpan<MeshInfo> meshes)
    {
        Span<PreparedInstance> visible = scene.VisibleSpan;
        int opaqueCount = 0, transparentCount = 0;
        for (int i = 0; i < visible.Length; i++)
        {
            ref PreparedInstance instance = ref visible[i];
            if ((uint)instance.Mesh >= (uint)meshes.Length)
                continue;
            if ((instance.Flags & RenderFlags.Transparent) != 0)
            {
                // Far range first, and back to front inside a range: the inverted distance bits sort ascending.
                int range = DepthRanges.Containing(instance.Depth);
                uint farFirst = ~BitConverter.SingleToUInt32Bits(MathF.Max(instance.Distance, 0f));
                Append(ref transparent, ref transparentCount, new DrawItem(((ulong)range << 32) | farFirst, i));
                continue;
            }
            int mask = DepthRanges.OpaqueMask(instance.Depth, instance.Radius);
            for (int range = 0; range < DepthRanges.Count; range++)
            {
                if ((mask & (1 << range)) != 0)
                    Append(ref opaque, ref opaqueCount, new DrawItem(((ulong)range << 32) | (uint)instance.Mesh, i));
            }
        }

        opaque.AsSpan(0, opaqueCount).Sort(default(DrawItemComparer));
        transparent.AsSpan(0, transparentCount).Sort(default(DrawItemComparer));
        EnsureCapacity(ref scene.Instances, opaqueCount + transparentCount);
        Emit(scene, meshes, opaque.AsSpan(0, opaqueCount), scene.Opaque);
        Emit(scene, meshes, transparent.AsSpan(0, transparentCount), scene.Transparent);
    }

    /// <summary>
    /// Writes the GPU instances in sorted order and one indirect command per run of equal (range, mesh), with
    /// firstInstance pointing at the run's first GPU instance.
    /// </summary>
    private static void Emit(PreparedScene scene, ReadOnlySpan<MeshInfo> meshes, ReadOnlySpan<DrawItem> items, CommandRange[] ranges)
    {
        int currentRange = -1, currentMesh = -1, rangeStart = scene.CommandCount;
        foreach (DrawItem item in items)
        {
            ref PreparedInstance instance = ref scene.Visible[item.Index];
            int range = (int)(item.Key >> 32);
            if (range != currentRange)
            {
                if (currentRange >= 0)
                    ranges[currentRange] = new CommandRange(rangeStart, scene.CommandCount - rangeStart);
                currentRange = range;
                currentMesh = -1;
                rangeStart = scene.CommandCount;
            }

            int gpuIndex = scene.InstanceCount++;
            scene.Instances[gpuIndex] = GpuInstance.From(instance.Position, instance.Rotation, instance.Scale, instance.Material);
            if (instance.Mesh == currentMesh)
            {
                scene.Commands[scene.CommandCount - 1].InstanceCount++;
                continue;
            }
            currentMesh = instance.Mesh;
            MeshInfo mesh = meshes[instance.Mesh];
            if (scene.CommandCount == scene.Commands.Length)
                Array.Resize(ref scene.Commands, scene.Commands.Length * 2);
            scene.Commands[scene.CommandCount++] = new DrawIndexedIndirectCommand
            {
                IndexCount = mesh.IndexCount,
                InstanceCount = 1,
                FirstIndex = mesh.FirstIndex,
                VertexOffset = mesh.VertexOffset,
                FirstInstance = (uint)gpuIndex,
            };
        }
        if (currentRange >= 0)
            ranges[currentRange] = new CommandRange(rangeStart, scene.CommandCount - rangeStart);
    }

    private static void CollectLights(RenderSnapshot snapshot, PreparedScene scene)
    {
        CameraView camera = scene.Camera;
        foreach (ref readonly RenderLight light in CollectionsMarshal.AsSpan(snapshot.Lights))
        {
            if (light.Range <= 0f || light.Intensity <= 0f)
                continue;
            Vector3 position = light.Position - camera.Offset;
            // A light whose sphere of influence is outside the frustum cannot reach a visible surface.
            if (camera.Culls(position, light.Range))
                continue;
            Vector3 direction = light.Direction.LengthSquared() > 1e-12f ? Vector3.Normalize(light.Direction) : -Vector3.UnitY;
            float cosOuter = MathF.Cos(Math.Clamp(light.OuterConeAngle, 0f, MathF.PI));
            float cosInner = MathF.Cos(Math.Clamp(light.InnerConeAngle, 0f, MathF.PI));
            // smoothstep needs a non-empty edge interval.
            cosInner = MathF.Max(cosInner, cosOuter + 1e-4f);
            if (scene.LightCount == scene.Lights.Length)
                Array.Resize(ref scene.Lights, scene.Lights.Length * 2);
            scene.Lights[scene.LightCount++] = new GpuLight
            {
                PositionRange = new Vector4(position, light.Range),
                RadianceKind = new Vector4(light.Color * light.Intensity, light.Kind == LightKind.Spot ? 1f : 0f),
                DirectionCosOuter = new Vector4(direction, cosOuter),
                Spot = new Vector4(cosInner, 0f, 0f, 0f),
            };
        }

        foreach (ref readonly RenderOccluder occluder in CollectionsMarshal.AsSpan(snapshot.Occluders))
        {
            if (occluder.Radius <= 0f)
                continue;
            if (scene.OccluderCount == scene.Occluders.Length)
                Array.Resize(ref scene.Occluders, scene.Occluders.Length * 2);
            scene.Occluders[scene.OccluderCount++] = new Vector4(occluder.Center - camera.Offset, occluder.Radius);
        }
    }

    /// <summary>The sun lights the scene: a non-zero direction and a positive intensity.</summary>
    internal static bool HasSun(in RenderSun sun) => sun.Direction.LengthSquared() > 1e-12f && sun.Intensity > 0f;

    private static void WriteConstants(RenderSnapshot snapshot, PreparedScene scene)
    {
        CameraView camera = scene.Camera;
        RenderSun sun = snapshot.Sun;
        bool hasSun = HasSun(sun);
        AmbientLight ambient = snapshot.Ambient;
        scene.Constants = new GpuFrameConstants
        {
            FarViewProj = scene.RangeViewProjection[DepthRanges.Far],
            MiddleViewProj = scene.RangeViewProjection[DepthRanges.Middle],
            NearViewProj = scene.RangeViewProjection[DepthRanges.Near],
            CameraRight = new Vector4(camera.Right * camera.TanHalfX, 0f),
            CameraUp = new Vector4(camera.Up * camera.TanHalfY, 0f),
            CameraForward = new Vector4(camera.Forward, 0f),
            SunDirection = new Vector4(hasSun ? Vector3.Normalize(sun.Direction) : Vector3.UnitY, MathF.Max(sun.AngularRadius, 0f)),
            SunRadiance = new Vector4(hasSun ? sun.Color * sun.Intensity : Vector3.Zero, 0f),
            AmbientSky = new Vector4(ambient.SkyColor * ambient.Intensity, 0f),
            AmbientGround = new Vector4(ambient.GroundColor * ambient.Intensity, 0f),
            Viewport = new Vector4(camera.Width, camera.Height, 1f / Math.Max(camera.Width, 1), 1f / Math.Max(camera.Height, 1)),
            Params = new Vector4(1f, (float)snapshot.Time, camera.PixelAngle, 0f),
            LightCount = (uint)scene.LightCount,
            OccluderCount = (uint)scene.OccluderCount,
            // The shadow pass fills in the cascades when it draws them.
            ShadowCascadeCount = 0,
            Flags = 0,
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
        public int Compare(DrawItem a, DrawItem b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Index.CompareTo(b.Index);
    }
}
