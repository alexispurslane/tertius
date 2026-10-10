using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// The render row of an entity. The snapshot builder draws an entity when <see cref="Flags"/> has
/// <see cref="RenderFlags.Visible"/> and <see cref="Mesh"/> is not none. Use <c>new RenderData { ... }</c>: the
/// parameterless constructor sets a unit scale, identity offsets and no mesh, while <c>default</c> zeroes everything.
/// </summary>
public struct RenderData
{
    /// <summary>The mesh to draw, or <see cref="MeshId.None"/>.</summary>
    public MeshId Mesh;
    /// <summary>The material of the mesh.</summary>
    public MaterialId Material;
    /// <summary>Scale along the entity's local axes.</summary>
    public Vector3 Scale;
    /// <summary>The radius of the bounding sphere, in meters, after scaling. Also the radius of a planet occluder.</summary>
    public float BoundingRadius;
    /// <summary>Drawing flags. Nothing is drawn without <see cref="RenderFlags.Visible"/>.</summary>
    public RenderFlags Flags;
    /// <summary>Used when this entity is the camera: the eye offset and look rotation relative to the entity.</summary>
    public Pose ViewOffset;
    /// <summary>A light that the entity carries.</summary>
    public LightData Light;

    /// <summary>No mesh, unit scale, identity view offset and no light.</summary>
    public RenderData()
    {
        Mesh = MeshId.None;
        Material = MaterialId.None;
        Scale = Vector3.One;
        ViewOffset = Pose.Identity;
        Light = new LightData();
    }
}

/// <summary>
/// A dynamic light carried by an entity. It sits at the entity's pose composed with <see cref="Offset"/>.
/// A spot light points along the local -Z axis of that pose.
/// </summary>
public struct LightData
{
    /// <summary>If false, the light is not in the render snapshot.</summary>
    public bool Enabled;
    /// <summary>Point or spot light.</summary>
    public LightKind Kind;
    /// <summary>Linear color.</summary>
    public Vector3 Color;
    /// <summary>Brightness multiplier of <see cref="Color"/>.</summary>
    public float Intensity;
    /// <summary>The distance at which the light falls to zero, in meters.</summary>
    public float Range;
    /// <summary>Half-angle of the full-intensity cone of a spot light, in radians.</summary>
    public float InnerConeAngle;
    /// <summary>Half-angle of the outer cone of a spot light, in radians.</summary>
    public float OuterConeAngle;
    /// <summary>The light's pose relative to the entity.</summary>
    public Pose Offset;

    /// <summary>A disabled white point light of range 10 m.</summary>
    public LightData()
    {
        Color = Vector3.One;
        Intensity = 1;
        Range = 10;
        InnerConeAngle = 0.3f;
        OuterConeAngle = 0.5f;
        Offset = Pose.Identity;
    }
}
