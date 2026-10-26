using System.Numerics;

namespace Tertius.Rendering;

/// <summary>A caster of a frame, interpolated once: an instance with RenderFlags.CastsShadow.</summary>
internal struct ShadowCaster
{
    /// <summary>Camera-relative position.</summary>
    public Vector3 Position;

    public Quaternion Rotation;
    public Vector3 Scale;

    /// <summary>The bounding radius, in world units.</summary>
    public float Radius;

    /// <summary>The distance of the center from the eye.</summary>
    public float Distance;

    public int Mesh;
    public uint EntityIndex;

    /// <summary>Which of the entity's render parts this is. With <see cref="EntityIndex"/>, it names the caster from frame to frame.</summary>
    public ushort Part;
}
