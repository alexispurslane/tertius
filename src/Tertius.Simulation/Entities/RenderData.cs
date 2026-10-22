using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// The render row of an entity. The snapshot builder draws an entity when <see cref="Flags"/> has
/// <see cref="RenderFlags.Visible"/> and <see cref="Mesh"/> is not none. Use <c>new RenderData { ... }</c>: the
/// parameterless constructor sets a unit scale, identity offsets and no mesh. <c>default</c> also has no mesh and no
/// material, because a default <see cref="MeshId"/> and <see cref="MaterialId"/> are none, but its scale is zero.
/// </summary>
public struct RenderData
{
    /// <summary>The mesh to draw, or <see cref="MeshId.None"/>.</summary>
    public MeshId Mesh;

    /// <summary>The material of the mesh, or <see cref="MaterialId.None"/> for the renderer's default material.</summary>
    public MaterialId Material;

    /// <summary>Scale along the entity's local axes.</summary>
    public Vector3 Scale;

    /// <summary>The radius of the bounding sphere, in meters, after scaling. Also the radius of a planet occluder.</summary>
    public float BoundingRadius;

    /// <summary>Drawing flags. Nothing is drawn without <see cref="RenderFlags.Visible"/>.</summary>
    public RenderFlags Flags;

    /// <summary>Used when this entity is the camera: the eye offset and look rotation relative to the entity.</summary>
    public Pose ViewOffset;

    /// <summary>The light that the entity carries: a point light, a spot light, or none, which is the default.</summary>
    public LightData Light;

    /// <summary>
    /// The puffs that the entity leaves behind it, or the default, which makes none (see <see cref="PuffEmitter"/>). The
    /// emitter of the front row is the emitter of the step that the next sync point closes.
    /// </summary>
    public PuffEmitter Emitter;

    /// <summary>No mesh, no material, unit scale, identity view offset, no light and no puffs.</summary>
    public RenderData()
    {
        Scale = Vector3.One;
        ViewOffset = Pose.Identity;
    }
}
