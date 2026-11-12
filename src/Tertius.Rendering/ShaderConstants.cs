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
    public bool MainView { get; init; }
    public bool NeighborLimitPrototype { get; init; }
    public bool ReflectionSharingPrototype { get; init; }

    public const uint NeighborLimitPrototypeId = 9;
    public const uint ReflectionSharingPrototypeId = 10;

    /// <summary>The constant_id of tileCornerMeans in shadows.glsl.</summary>
    public const uint TileCornerMeansId = 11;

    /// <summary>
    /// True when the filter of a local shadow tile reads sixteen corner means, false when it reads nine gathers
    /// (<see cref="LocalShadowReads"/>). The renderer sets it for every shading pipeline from its atlas's reads on its
    /// driver. A pipeline with constants of its own keeps the gathers, which give the same shadow.
    /// </summary>
    public bool TileCornerMeans { get; init; }

    /// <summary>The constant_id of environmentMapOn in environment.glsl.</summary>
    public const uint EnvironmentMapId = 0;

    /// <summary>The constant_id of cutoutAlpha in material.glsl.</summary>
    public const uint CutoutAlphaId = 1;

    /// <summary>The constant_id of scatterTapsPerSide in subsurface.glsl.</summary>
    public const uint ScatterTapsPerSideId = 2;

    /// <summary>The constant_id of dropsFibers in frame.glsl.</summary>
    public const uint DropsFibersId = 3;

    /// <summary>The constant_id of writesMotion in mesh.vert.</summary>
    public const uint WritesMotionId = 4;

    /// <summary>The constant_id of rayKind in screen_trace_block_rays.comp and screen_trace_own_rays.comp.</summary>
    public const uint RayKindId = 5;

    /// <summary>The constant_id of screenTraceOn in temporal.glsl.</summary>
    public const uint ScreenTraceOnId = 6;

    /// <summary>The constant_id of mainView in material.glsl.</summary>
    public const uint MainViewId = 8;

    /// <summary>
    /// True in the cutout pipeline of an opaque pass, whose material sample discards a texel below its material's cutoff
    /// (docs/plans/skin-and-hair.md, "The cutout test"); false in the solid pipelines, which keep their early depth writes.
    /// </summary>
    public bool CutoutAlpha { get; init; }

    /// <summary>
    /// True when the pipeline's shaders drop the code that shades a fiber: the hair branch of the material sample and the
    /// fiber's light loop (docs/plans/skin-and-hair.md, "Cost"). A pipeline that sets it must never draw a hair material,
    /// which would then shade as a surface. Only the one-sided variant of a <see cref="SidedPipeline"/> sets it: only a
    /// hair material draws both sides (<see cref="MaterialStore.DrawsBothSides"/>), so no hair material reaches that
    /// variant.
    /// </summary>
    public bool DropsFibers { get; init; }

    /// <summary>
    /// True in the G-buffer's pipelines, whose vertex shader reads the motion rows and the kept positions and passes the
    /// motion target's two places on (docs/plans/temporal-renderer.md, "Jitter"). The other passes that draw with mesh.vert
    /// write no motion.
    /// </summary>
    public bool WritesMotion { get; init; }

    /// <summary>
    /// The skin scatter's taps per side (docs/plans/skin-and-hair.md, "The two passes"), which only
    /// <see cref="SkinScatterPass"/> sets, from <see cref="ScatterImages.Setting"/>. Every other pipeline keeps
    /// <see cref="NoSubsurface"/>: the direct pass chooses its shader by the diffuse image
    /// (<see cref="RenderTargets.Diffuse"/>) instead, and the ambient pass by the scatter images.
    /// </summary>
    public SubsurfaceSetting Subsurface { get; init; }

    /// <summary>
    /// The ray kind of a pipeline of the screen tracer's rays: 0 reflection rays, 1 diffuse rays (screen_trace.glsl), which
    /// only <see cref="ScreenTraceKernel"/> sets.
    /// </summary>
    public uint RayKind { get; init; }

    /// <summary>
    /// True under <see cref="ScreenTrace"/>, whose signals' slow histories the composite reads; false under <see
    /// cref="NoScreenTrace"/>, under which it computes the closed forms.
    /// </summary>
    public bool ScreenTraceOn { get; init; }

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
        // these constants ignores their entries (the Vulkan spec, VkSpecializationMapEntry). Each row's value sits at the
        // offset of its index.
        int tapsPerSide = values.Subsurface switch
        {
            NoSubsurface => 1,
            SeparableSubsurface separable => separable.TapsPerSide,
        };
        ReadOnlySpan<(uint Id, uint Value)> rows =
        [
            (EnvironmentMapId, VkBool(values.EnvironmentMap)),
            (CutoutAlphaId, VkBool(values.CutoutAlpha)),
            (ScatterTapsPerSideId, (uint)tapsPerSide),
            (DropsFibersId, VkBool(values.DropsFibers)),
            (WritesMotionId, VkBool(values.WritesMotion)),
            (RayKindId, values.RayKind),
            (ScreenTraceOnId, VkBool(values.ScreenTraceOn)),
            (MainViewId, VkBool(values.MainView)),
            (NeighborLimitPrototypeId, VkBool(values.NeighborLimitPrototype)),
            (ReflectionSharingPrototypeId, VkBool(values.ReflectionSharingPrototype)),
            (TileCornerMeansId, VkBool(values.TileCornerMeans)),
        ];
        for (int i = 0; i < rows.Length; i++)
        {
            storage->Data[i] = rows[i].Value;
            storage->Entries[i] = new(rows[i].Id, (uint)(i * sizeof(uint)), sizeof(uint));
        }

        storage->Info = new()
        {
            MapEntryCount = (uint)rows.Length,
            PMapEntries = (SpecializationMapEntry*)&storage->Entries,
            DataSize = (nuint)(rows.Length * sizeof(uint)),
            PData = &storage->Data,
        };
        return &storage->Info;
    }

    /// <summary>The VkBool32 of <paramref name="value"/>: 1 or 0.</summary>
    private static uint VkBool(bool value) => value ? 1u : 0u;
}

/// <summary>
/// Storage for one pipeline's specialization: the values, their map entries, and the info that points at both. The method
/// that creates a pipeline holds one as a local, so the pointers stay valid until the pipeline exists.
/// </summary>
internal struct SpecializationStorage
{
    /// <summary>
    /// The number of constants: one per field of <see cref="ShaderConstants"/>, the subsurface setting's being its taps per
    /// side. <see cref="ShaderConstants.Specialization"/> writes one row per constant, and a row past this count throws.
    /// </summary>
    public const int Count = 11;

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
