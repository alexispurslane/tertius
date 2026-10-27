using System.Runtime.CompilerServices;
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

    /// <summary>The constant_id of cutoutAlpha in material.glsl.</summary>
    public const uint CutoutAlphaId = 1;

    /// <summary>The constant_id of scatterTapsPerSide in subsurface.glsl.</summary>
    public const uint ScatterTapsPerSideId = 2;

    /// <summary>
    /// True in the cutout pipeline of an opaque pass, whose material sample discards a texel below its material's cutoff
    /// (docs/plans/skin-and-hair.md, "The cutout test"); false in the solid pipelines, which keep their early depth writes.
    /// </summary>
    public bool CutoutAlpha { get; init; }

    /// <summary>
    /// The skin scatter's taps per side (docs/plans/skin-and-hair.md, "The two passes"), which only
    /// <see cref="SkinScatterPass"/> sets, from <see cref="ScatterImages.Setting"/>. Every other pipeline keeps
    /// <see cref="NoSubsurface"/>: the lighting passes choose their shaders by the scatter images instead.
    /// </summary>
    public SubsurfaceSetting Subsurface { get; init; }

    /// <summary>
    /// The specialization info of <paramref name="constants"/>, filled into <paramref name="storage"/>, or null for a
    /// pipeline whose shaders declare none.
    /// </summary>
    public static unsafe SpecializationInfo* Specialization(ShaderConstants? constants, SpecializationStorage* storage)
    {
        if (constants is not { } values)
            return null;
        // VkBool32s, the type of a bool specialization constant, and an int, at the offsets of their entries. Without the
        // scatter no shader reads the taps per side, and 1 is the shader's own default.
        int tapsPerSide = values.Subsurface switch
        {
            NoSubsurface => 1,
            SeparableSubsurface separable => separable.TapsPerSide,
        };
        storage->Data[0] = values.EnvironmentMap ? 1u : 0u;
        storage->Data[1] = values.CutoutAlpha ? 1u : 0u;
        storage->Data[2] = (uint)tapsPerSide;
        storage->Entries[0] = new(EnvironmentMapId, 0, sizeof(uint));
        storage->Entries[1] = new(CutoutAlphaId, sizeof(uint), sizeof(uint));
        storage->Entries[2] = new(ScatterTapsPerSideId, 2 * sizeof(uint), sizeof(uint));
        storage->Info = new()
        {
            MapEntryCount = SpecializationStorage.Count,
            PMapEntries = (SpecializationMapEntry*)&storage->Entries,
            DataSize = SpecializationStorage.Count * sizeof(uint),
            PData = &storage->Data,
        };
        return &storage->Info;
    }
}

/// <summary>
/// Storage for one pipeline's specialization: the values, their map entries, and the info that points at both. The method
/// that creates a pipeline holds one as a local, so the pointers stay valid until the pipeline exists.
/// </summary>
internal struct SpecializationStorage
{
    /// <summary>
    /// The number of constants: one per field of <see cref="ShaderConstants"/>, the subsurface setting's being its taps per
    /// side.
    /// </summary>
    public const int Count = 3;

    public SpecializationValues Data;
    public SpecializationEntries Entries;
    public SpecializationInfo Info;
}

/// <summary>The values of <see cref="SpecializationStorage"/>, one 32-bit word per constant, in the order of their entries.</summary>
[InlineArray(SpecializationStorage.Count)]
internal struct SpecializationValues
{
    private uint first;
}

/// <summary>The map entries of <see cref="SpecializationStorage"/>.</summary>
[InlineArray(SpecializationStorage.Count)]
internal struct SpecializationEntries
{
    private SpecializationMapEntry first;
}
