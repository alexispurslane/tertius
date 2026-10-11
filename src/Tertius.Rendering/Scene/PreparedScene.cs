using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>The interpolated camera of one frame. World space is camera-relative: the eye is at the origin.</summary>
internal struct CameraView
{
    public Quaternion Rotation;
    public Vector3 Right;
    public Vector3 Up;
    public Vector3 Forward;
    /// <summary>The interpolated eye position relative to the render origin. Subtracted from every position.</summary>
    public Vector3 Offset;
    public float VerticalFov;
    public float Aspect;
    public float TanHalfX;
    public float TanHalfY;
    public int Width;
    public int Height;
    /// <summary>World to view rotation, in System.Numerics row-vector form.</summary>
    public Matrix4x4 View;
    /// <summary>The root frame's axes in world axes, interpolated like <see cref="Rotation"/>.</summary>
    public Quaternion RootRotation;

    /// <summary>The angle that one pixel covers at the center of the screen.</summary>
    public readonly float PixelAngle => 2f * TanHalfY / Math.Max(Height, 1);

    /// <summary>World to clip for one depth range, in System.Numerics row-vector form. See <see cref="DepthRanges.RangeProjection"/>.</summary>
    public readonly Matrix4x4 ViewProjection(int range) => View * DepthRanges.RangeProjection(range, VerticalFov, Aspect);

    /// <summary>True when a sphere is completely outside the four side planes or behind the near plane.</summary>
    public readonly bool Culls(Vector3 center, float radius)
    {
        float depth = Vector3.Dot(center, Forward);
        if (depth + radius < DepthRanges.NearPlane)
            return true;
        float x = Vector3.Dot(center, Right);
        float y = Vector3.Dot(center, Up);
        // A side plane through the eye has normal (±1, 0, −tan) / √(1 + tan²) in (x, y, depth) coordinates.
        float scaleX = MathF.Sqrt(1 + TanHalfX * TanHalfX);
        float scaleY = MathF.Sqrt(1 + TanHalfY * TanHalfY);
        return MathF.Abs(x) - depth * TanHalfX > radius * scaleX ||
               MathF.Abs(y) - depth * TanHalfY > radius * scaleY;
    }
}

/// <summary>An instance after interpolation and culling, before batching.</summary>
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
    public uint EntityIndex;
    /// <summary>Which of the entity's render parts this is. With <see cref="EntityIndex"/>, it names the object from frame to frame.</summary>
    public ushort Part;
}

/// <summary>A run of indirect commands for one draw call.</summary>
internal readonly record struct CommandRange(int First, int Count);

/// <summary>A run of GPU instances for one instanced draw.</summary>
internal readonly record struct InstanceRange(int First, int Count);

/// <summary>
/// Everything the CPU prepares for one frame: camera, visible instances, the sorted GPU instance array, indirect
/// commands per depth range and per shadow cascade, lights, occluders and the frame constants. Arrays are reused
/// from frame to frame.
/// </summary>
internal sealed class PreparedScene
{
    public CameraView Camera;
    /// <summary>The snapshot's sun, as given. Instance processors read it, for example to light impostor captures.</summary>
    public RenderSun Sun;
    /// <summary>The snapshot's ambient light, as given.</summary>
    public AmbientLight Ambient;
    public GpuFrameConstants Constants;
    public readonly Matrix4x4[] RangeViewProjection = new Matrix4x4[DepthRanges.Count];

    /// <summary>Visible instances. Instance processors may edit this list before batching.</summary>
    public PreparedInstance[] Visible = new PreparedInstance[256];
    public int VisibleCount;

    /// <summary>GPU instances in draw order. An opaque object in two depth ranges appears twice.</summary>
    public GpuInstance[] Instances = new GpuInstance[256];
    public int InstanceCount;

    public DrawIndexedIndirectCommand[] Commands = new DrawIndexedIndirectCommand[64];
    public int CommandCount;

    /// <summary>Opaque commands per depth range, indexed by <see cref="DepthRanges"/>.</summary>
    public readonly CommandRange[] Opaque = new CommandRange[DepthRanges.Count];
    /// <summary>Transparent commands per depth range, sorted back to front across all ranges.</summary>
    public readonly CommandRange[] Transparent = new CommandRange[DepthRanges.Count];

    public GpuLight[] Lights = new GpuLight[16];
    public int LightCount;
    /// <summary>
    /// The far shadows: xyz camera-relative center, w the radius of the caster's bounding sphere (see
    /// <see cref="Eclipse.SunVisibility"/>). Those of casters in the shadow maps come first.
    /// </summary>
    public Vector4[] Occluders = new Vector4[8];
    public int OccluderCount;
    /// <summary>How many far shadows, at the front of <see cref="Occluders"/>, belong to casters that the shadow maps also draw.</summary>
    public int MappedOccluderCount;

    /// <summary>Instances in the snapshot, before any filtering.</summary>
    public int SubmittedInstances;

    /// <summary>The sun shadow cascades of this frame. Valid when <see cref="ShadowCascadeCount"/> is not 0.</summary>
    public ShadowCascadeArray Cascades;
    /// <summary>Cascades the shadow pass renders and the lighting pass samples: 0 or <see cref="ShadowCascades.Count"/>.</summary>
    public int ShadowCascadeCount;
    /// <summary>Caster commands per cascade, appended after batching by the shadow pass.</summary>
    public readonly CommandRange[] ShadowCommands = new CommandRange[ShadowCascades.Count];
    /// <summary>Distinct instances selected as shadow casters.</summary>
    public int ShadowCasters;
    /// <summary>Caster instances over all cascades: a caster in two cascades counts twice.</summary>
    public int ShadowInstances;

    /// <summary>Impostor billboards per depth range, appended after batching by the impostor pass.</summary>
    public readonly InstanceRange[] Impostors = new InstanceRange[DepthRanges.Count];

    public Span<PreparedInstance> VisibleSpan => Visible.AsSpan(0, VisibleCount);

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

    /// <summary>Adds a GPU instance after batching, for a pass's own draws. Returns its index for firstInstance.</summary>
    public int AppendInstance(in GpuInstance instance)
    {
        if (InstanceCount == Instances.Length)
            Array.Resize(ref Instances, Instances.Length * 2);
        Instances[InstanceCount] = instance;
        return InstanceCount++;
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
        OccluderCount = 0;
        MappedOccluderCount = 0;
        SubmittedInstances = 0;
        Array.Clear(Opaque);
        Array.Clear(Transparent);
        ShadowCascadeCount = 0;
        Array.Clear(ShadowCommands);
        ShadowCasters = 0;
        ShadowInstances = 0;
        Array.Clear(Impostors);
    }
}

/// <summary>
/// Edits the visible instances after interpolation, culling, lights and occluders, and before batching, on the render
/// thread. <see cref="ImpostorManager"/> is one: it takes the objects it draws as impostors out of
/// <see cref="PreparedScene.Visible"/> and queues atlas captures, which <see cref="ImpostorPass"/> renders in
/// <see cref="FrameGraph.Stage.ImpostorCaptures"/> before the G-buffer pass. Register processors with
/// <see cref="SceneBatcher.Processors"/>.
/// </summary>
internal interface IInstanceProcessor
{
    void Process(PreparedScene scene);
}
