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

    /// <summary>The constant_id of shadesFibers in frame.glsl.</summary>
    public const uint ShadesFibersId = 3;

    /// <summary>
    /// True in the cutout pipeline of an opaque pass, whose material sample discards a texel below its material's cutoff
    /// (docs/plans/skin-and-hair.md, "The cutout test"); false in the solid pipelines, which keep their early depth writes.
    /// </summary>
    public bool CutoutAlpha { get; init; }

    /// <summary>
    /// True in the one-sided variant of a <see cref="SidedPipeline"/>, false in every other pipeline. Only a hair material
    /// draws both sides (<see cref="MaterialStore.DrawsBothSides"/>), so no hair material reaches that variant, and its
    /// shaders drop the code that shades a fiber: the hair branch of the material sample and the fiber's light loop
    /// (docs/plans/skin-and-hair.md, "Cost"). The shaders read it as shadesFibers, its negation.
    /// </summary>
    public bool OneSided { get; init; }

    /// <summary>
    /// The skin scatter's taps per side (docs/plans/skin-and-hair.md, "The two passes"), which only
    /// <see cref="SkinScatterPass"/> sets, from <see cref="ScatterImages.Setting"/>. Every other pipeline keeps
    /// <see cref="NoSubsurface"/>: the direct pass chooses its shader by the diffuse image
    /// (<see cref="RenderTargets.Diffuse"/>) instead, and the ambient pass by the scatter images.
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
        // scatter no shader reads the taps per side, and 1 is the shader's own default. A shader that declares none of
        // these constants ignores their entries (the Vulkan spec, VkSpecializationMapEntry).
        int tapsPerSide = values.Subsurface switch
        {
            NoSubsurface => 1,
            SeparableSubsurface separable => separable.TapsPerSide,
        };
        storage->Data[0] = values.EnvironmentMap ? 1u : 0u;
        storage->Data[1] = values.CutoutAlpha ? 1u : 0u;
        storage->Data[2] = (uint)tapsPerSide;
        storage->Data[3] = values.OneSided ? 0u : 1u;
        storage->Entries[0] = new(EnvironmentMapId, 0, sizeof(uint));
        storage->Entries[1] = new(CutoutAlphaId, sizeof(uint), sizeof(uint));
        storage->Entries[2] = new(ScatterTapsPerSideId, 2 * sizeof(uint), sizeof(uint));
        storage->Entries[3] = new(ShadesFibersId, 3 * sizeof(uint), sizeof(uint));
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
    public const int Count = 4;

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
