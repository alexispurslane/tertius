using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The constants that the shading pipelines are made with, fixed for the renderer's life: Vulkan specialization
/// constants (docs/plans/environment-map.md, decision 29). <see cref="GraphicsPipelineDesc.Constants"/> sets them on
/// both stages of a pipeline, and <see cref="ComputePipelineDesc.Constants"/> on its one stage.
/// </summary>
/// <param name="EnvironmentMap">True while the setting is <see cref="SceneEnvironmentMap"/>: the map is the ambient light.</param>
internal readonly record struct ShaderConstants(bool EnvironmentMap)
{
    /// <summary>The constant_id of environmentMapOn in environment.glsl.</summary>
    public const uint EnvironmentMapId = 0;

    /// <summary>
    /// The specialization info of <paramref name="constants"/>, filled into <paramref name="storage"/>, or null for a
    /// pipeline whose shaders declare none.
    /// </summary>
    public static unsafe SpecializationInfo* Specialization(ShaderConstants? constants, SpecializationStorage* storage)
    {
        if (constants is not { } values)
            return null;
        // A VkBool32, the type of a bool specialization constant.
        storage->Data = values.EnvironmentMap ? 1u : 0u;
        storage->Entry = new(EnvironmentMapId, 0, sizeof(uint));
        storage->Info = new()
        {
            MapEntryCount = 1,
            PMapEntries = &storage->Entry,
            DataSize = sizeof(uint),
            PData = &storage->Data,
        };
        return &storage->Info;
    }
}

/// <summary>
/// Storage for one pipeline's specialization: the values, their map entry, and the info that points at both. The method
/// that creates a pipeline holds one as a local, so the pointers stay valid until the pipeline exists.
/// </summary>
internal struct SpecializationStorage
{
    public uint Data;
    public SpecializationMapEntry Entry;
    public SpecializationInfo Info;
}
