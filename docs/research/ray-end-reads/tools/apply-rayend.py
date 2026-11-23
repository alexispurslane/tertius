# Tool code of ter-pdw8 (what a ray's end reads). Applies the ray-end variants to a plain build copy of 3549d3283 that
# already holds docs/research/ray-census/census-probe.patch. The edits never enter src. The variant is the
# specialization constant 40, from TERTIUS_PROBE_RAY_END: 0 today (V0), 1 the running mean of the hit light (V1's
# stand-in), 2 the hit light's mip level of the ray's footprint (V2), 3 both (V3).
# Usage: python3 -I apply-rayend.py <build copy root>
import os, shutil, sys

root = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))


def edit(rel, old, new):
    path = os.path.join(root, rel)
    s = open(path).read()
    assert s.count(old) == 1, (rel, old[:80])
    open(path, 'w').write(s.replace(old, new))


glsl = 'src/Tertius.Rendering/Shaders/screen_trace.glsl'
# The variant, and the mip atlas of the hit light.
edit(glsl, """layout(set = 3, binding = 26, rgba16f) uniform image2DArray hitLight;
""", """layout(set = 3, binding = 26, rgba16f) uniform image2DArray hitLight;

// PROBE (ter-pdw8, never commit to src): what a ray's end reads. Bit 0: the running mean of the hit light (layers 2 and
// 3) in place of this draw's (layers 0 and 1). Bit 1: the level of the hit light's mip atlas that matches the ray's
// footprint at the hit, in place of the hit pixel.
layout(constant_id = 40) const uint rayEndVariant = 0u;

// The rays that one pixel's estimate of a kind averages, the N of the ray's solid angle 1 / (N density): one ray per
// pixel per draw, over the histories' cap of 32 draws (SignalParameters.Cap).
const float ProbeRaysPerEstimate = 32.0;

// The width B of the render allocation: the image is B + ceil(B / 2) wide, level 0 at x 0 and every later level at x B.
int probeAtlasBase()
{
    return (2 * imageSize(hitLight).x) / 3;
}

// The cells of level k over the viewport: each side halved and rounded up, k times.
ivec2 probeLevelSize(int level)
{
    return (ivec2(frame.viewport.xy) + (1 << level) - 1) >> level;
}

// The atlas texel of cell `cell` of level k: level 0 is the hit light itself, and level k from 1 lies at x B, below
// the levels 1 to k - 1, each as high as the allocation's height H halved and rounded up that often. Those heights add
// up to at most H plus the number of levels, so the image is H + 16 rows high (ScreenTraceImages).
ivec2 probeAtlasOf(int level, ivec2 cell)
{
    if (level == 0)
        return cell;
    int top = 0;
    int height = imageSize(hitLight).y - 16;
    for (int k = 1; k < level; k++)
        top += (height + (1 << k) - 1) >> k;
    return ivec2(probeAtlasBase() + cell.x, top + cell.y);
}

// The coarsest level: one cell covers the viewport.
int probeTopLevel()
{
    ivec2 v = ivec2(frame.viewport.xy);
    return int(ceil(log2(float(max(max(v.x, v.y), 2)))));
}
""")
edit(glsl, """vec3 hitLightOf(ivec2 q, uint kind)
{
    return imageLoad(hitLight, ivec3(q, kind)).rgb;
}
""", """vec3 hitLightOf(ivec2 q, uint kind)
{
    return imageLoad(hitLight, ivec3(q, kind)).rgb;
}

// PROBE (ter-pdw8): the hit light that the variant reads at the hit pixel q for a ray whose footprint at the hit spans
// `footprint` render pixels: level round(log2(footprint)), clamped to the atlas's levels, of the chosen layer.
vec3 probeHitLightOf(ivec2 q, uint kind, float footprint)
{
    int layer = int(kind) + ((rayEndVariant & 1u) != 0u ? 2 : 0);
    int level = 0;
    if ((rayEndVariant & 2u) != 0u)
        level = clamp(int(floor(log2(max(footprint, 1.0)) + 0.5)), 0, probeTopLevel());
    vec4 value = imageLoad(hitLight, ivec3(probeAtlasOf(level, q >> level), layer));
    // The mean layers hold the light where covered and the coverage: their light over the draws is the product.
    return (rayEndVariant & 1u) != 0u ? value.rgb * value.a : value.rgb;
}
""")
edit(glsl, """    bool traced;      // false for a direction below the pixel's geometric plane, which brings no light
""", """    bool traced;      // false for a direction below the pixel's geometric plane, which brings no light
    bool probeGuided; // PROBE (ter-pdw8): the ray took the guided draw
""")
edit(glsl, """    bool guided = guiding && unitOf(draw.z) < trace.guidedShare;
""", """    bool guided = guiding && unitOf(draw.z) < trace.guidedShare;
    result.probeGuided = guided;
""")
edit(glsl, """    result.light = mix(missLight, hitLightOf(hitPixel, kind), fade);
""", """    // PROBE (ter-pdw8): the ray's footprint at the hit in render pixels: the hit distance in m times the square root of
    // the ray's solid angle 1 / (N density) in sr, over the size in m of one pixel at the hit's distance from the eye.
    float probeOmega = 1.0 / max(ProbeRaysPerEstimate * result.density, 1e-6);
    float probeFootprint = result.distance * sqrt(probeOmega)
        / max(texelFetch(gbufferDistance, hitPixel, 0).r * frame.params.z, 1e-9);
    result.light = mix(
        missLight,
        rayEndVariant == 0u ? hitLightOf(hitPixel, kind) : probeHitLightOf(hitPixel, kind, probeFootprint),
        fade
    );
""")
# The guided flag rides in the sign of the record's hit distance: the ray words are 16-bit (RayFormat), and a set bit
# above them saturated the density word to a NaN on this Mac, which dropped every guided ray.
edit(glsl, """    record.light = vec4(traced.light, min(traced.distance, MostHitDistance));
""", """    record.light = vec4(traced.light, min(traced.distance, MostHitDistance));
    // PROBE (ter-pdw8): a guided ray's record holds its hit distance negated, and a guided miss -MostHitDistance.
    if (traced.probeGuided)
        record.light.a = traced.distance > 0.0 ? -record.light.a : -MostHitDistance;
""")
# No-ray records: the flag is in the TracedRay only; noRay() leaves ray 0.

plan = 'src/Tertius.Rendering/Shaders/screen_trace_plan.comp'
edit(plan, """void storeHitLight(ivec2 pixel, LeavingLight leaving)
{
    imageStore(hitLight, ivec3(pixel, KindReflection), vec4(leaving.reflection, 0.0));
    imageStore(hitLight, ivec3(pixel, KindDiffuse), vec4(leaving.diffuse, 0.0));
""", """// PROBE (ter-pdw8): this draw's hit light of the pixel and whether a walk can hit it, for the running means.
vec3 probeReflection;
vec3 probeDiffuse;
float probeCovered;

void storeHitLight(ivec2 pixel, LeavingLight leaving)
{
    // PROBE (ter-pdw8): alpha 1 marks a pixel that a walk can hit, for the mip atlas.
    imageStore(hitLight, ivec3(pixel, KindReflection), vec4(leaving.reflection, 1.0));
    imageStore(hitLight, ivec3(pixel, KindDiffuse), vec4(leaving.diffuse, 1.0));
    probeReflection = leaving.reflection;
    probeDiffuse = leaving.diffuse;
    probeCovered = 1.0;
""")
edit(plan, """void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(vec2(pixel), frame.viewport.xy)))
        return;
""", """// PROBE (ter-pdw8): layers 2 and 3 hold V1's stand-in, the hit light resolved over the recent draws: with n the draws
// since the last reset, at most 64 (more than two jitter periods at every size of the record; from 64 on, a running
// mean with the weight 1/64, so that the dark first draws of a young history leave it), S the running mean of the light times the coverage (1 where a walk can hit the pixel,
// 0 for the sky) and C the running mean of the coverage, rgb holds S / C (the light where covered, 0 if never) and
// alpha holds C. So rgb times alpha is the pixel's light over the draws with the sky as 0, as an upscaled pixel holds
// its surfaces' part, and a thin bright feature that a jitter phase alone puts on a pixel counts by its share.
void probeUpdateMeans(ivec2 pixel)
{
    float count = min(float(trace.drawIndex) + 1.0, 64.0);
    for (int kind = 0; kind < 2; kind++)
    {
        vec3 light = (kind == 0 ? probeReflection : probeDiffuse) * probeCovered;
        vec4 last = imageLoad(hitLight, ivec3(pixel, 2 + kind));
        vec3 sum = last.rgb * last.a;
        float covered = last.a;
        if (trace.drawIndex == 0u || !(covered >= 0.0 && covered <= 1.0))
        {
            sum = light;
            covered = probeCovered;
        }
        else
        {
            sum += (light - sum) / count;
            covered += (probeCovered - covered) / count;
        }
        imageStore(hitLight, ivec3(pixel, 2 + kind), vec4(covered > 0.0 ? sum / covered : vec3(0.0), covered));
    }
}

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(vec2(pixel), frame.viewport.xy)))
        return;
    probeReflection = vec3(0.0); // PROBE (ter-pdw8)
    probeDiffuse = vec3(0.0);
    probeCovered = 0.0;
""")
edit(plan, """    // An impostor sends its glow alone. The sky's hit light stays as it is, because no walk hits it.
    else if (hittable(pixel))
        storeHitLight(pixel, LeavingLight(glowAt(pixel), glowAt(pixel)));
    imageStore(rayPlan, pixel, uvec4(plan));
""", """    // An impostor sends its glow alone. The sky's hit light stays as it is, because no walk hits it.
    else if (hittable(pixel))
        storeHitLight(pixel, LeavingLight(glowAt(pixel), glowAt(pixel)));
    // PROBE (ter-pdw8): the sky covers no cell of the mip atlas.
    else
    {
        imageStore(hitLight, ivec3(pixel, KindReflection), vec4(0.0));
        imageStore(hitLight, ivec3(pixel, KindDiffuse), vec4(0.0));
    }
    probeUpdateMeans(pixel);
    imageStore(rayPlan, pixel, uvec4(plan));
""")
shutil.copy(os.path.join(here, 'screen_trace_hit_mips.comp'),
            os.path.join(root, 'src/Tertius.Rendering/Shaders/screen_trace_hit_mips.comp'))

# The census code of the resolve: 64 more for a guided diffuse ray.
resolve = 'src/Tertius.Rendering/Shaders/screen_trace_resolve.comp'
edit(resolve, """    float censusDistance = diffuse.light.a;""",
     """    bool censusGuided = diffuse.light.a < 0.0; // PROBE (ter-pdw8): the sign marks a guided ray
    float censusDistance = diffuse.light.a <= -MostHitDistance ? 0.0 : abs(diffuse.light.a);""")
edit(resolve, """censusBin + censusRatio / 16.0));""",
     """censusBin + (censusGuided ? 64.0 : 0.0) + censusRatio / 16.0));""")
edit(resolve, """    float hitDistance = diffuse.light.a;""",
     """    float hitDistance = diffuse.light.a <= -MostHitDistance ? 0.0 : abs(diffuse.light.a); // PROBE (ter-pdw8)""")

# C#: the image, the barrier, the pass, the constant.
edit('src/Tertius.Rendering/Frame/ScreenTraceImages.cs',
     """HitLight = CreateImage(extent: extent, format: HitLightFormat, layers: Kinds, name: "screen trace hit light");""",
     """// PROBE (ter-pdw8): four layers (the running means in 2 and 3) and room for the mip atlas to the right.
        // The levels' heights add up to more than the height, so 16 more rows hold the deepest levels.
        HitLight = CreateImage(
            extent: new Extent2D(extent.Width + (extent.Width + 1) / 2, extent.Height + 16),
            format: HitLightFormat,
            layers: 4,
            name: "screen trace hit light"
        );""")
edit('src/Tertius.Rendering/Passes/ScreenTracePass.cs',
     """        barriers.Image(images.HitLight, ImageUse.StorageCompute, discard: true);""",
     """        barriers.Image(images.HitLight, ImageUse.StorageCompute); // PROBE (ter-pdw8): keeps the running means""")
edit('src/Tertius.Rendering/Passes/ScreenTracePass.cs',
     """    public static void DeclareSurfaces(FrameContext context)
""", """    // PROBE (ter-pdw8): levels 1 to the top of the hit light's mip atlas, one dispatch each, trace.drawIndex = level.
    public void RecordLevels(FrameContext context)
    {
        context.BeginComputePass(
            pipeline: pipeline,
            layout: layouts.ScreenTraceLayout,
            passSet: images.Sets[context.History.Parity]
        );
        Extent2D extent = context.Extent;
        int top = (int)Math.Ceiling(Math.Log2(Math.Max(Math.Max(extent.Width, extent.Height), 2u)));
        for (int level = 1; level <= top; level++)
        {
            var push = ScreenTracePushConstants.Of(setting, level, images.Guided);
            context.Vk.CmdPushConstants(
                commandBuffer: context.CommandBuffer,
                layout: layouts.ScreenTraceLayout,
                stageFlags: ShaderStageFlags.ComputeBit,
                offset: 0,
                size: (uint)sizeof(ScreenTracePushConstants),
                pValues: &push
            );
            uint width = (extent.Width + (1u << level) - 1) >> level,
                height = (extent.Height + (1u << level) - 1) >> level;
            context.Vk.CmdDispatch(
                commandBuffer: context.CommandBuffer,
                groupCountX: (width + GroupSize - 1) / GroupSize,
                groupCountY: (height + GroupSize - 1) / GroupSize,
                groupCountZ: 1
            );
            var barrier = new MemoryBarrier(
                srcAccessMask: AccessFlags.ShaderWriteBit,
                dstAccessMask: AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit
            );
            context.Vk.CmdPipelineBarrier(
                context.CommandBuffer,
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.ComputeShaderBit,
                0,
                1,
                &barrier,
                0,
                null,
                0,
                null
            );
        }
    }

    public static void DeclareSurfaces(FrameContext context)
""")
edit('src/Tertius.Rendering/Passes/ScreenTracePass.cs',
     """internal sealed class ScreenTracePlanPass(ScreenTraceKernel kernel, ScreenTraceImages images) : RenderPass
""", """// PROBE (ter-pdw8): builds the hit light's mip atlas after the plan, for the variants that read it.
internal sealed class ScreenTraceHitMipsPass(ScreenTraceKernel kernel, ScreenTraceImages images) : RenderPass
{
    public static readonly uint Variant = uint.TryParse(
        Environment.GetEnvironmentVariable("TERTIUS_PROBE_RAY_END"),
        out uint variant
    )
        ? variant
        : 0u;

    public override string Name => kernel.Name;

    public override void Record(FrameContext context)
    {
        if ((Variant & 2u) == 0u)
            return;
        context.Barriers.Image(images.HitLight, ImageUse.StorageCompute);
        kernel.RecordLevels(context);
    }

    public override void Dispose() => kernel.Dispose();
}

internal sealed class ScreenTracePlanPass(ScreenTraceKernel kernel, ScreenTraceImages images) : RenderPass
""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """            new ScreenTracePlanPass(Kernel("ray plan", "screen_trace_plan.comp"), images)
        );
""", """            new ScreenTracePlanPass(Kernel("ray plan", "screen_trace_plan.comp"), images)
        );
        // PROBE (ter-pdw8)
        Graph.Add(
            FrameGraph.Stage.ScreenTrace,
            new ScreenTraceHitMipsPass(Kernel("hit light mips", "screen_trace_hit_mips.comp"), images)
        );
""")
edit('src/Tertius.Rendering/ShaderConstants.cs',
     """            (12u, uint.TryParse(Environment.GetEnvironmentVariable("TERTIUS_PROBE_SIGNAL"), out uint probeSignal) ? probeSignal : 0u),
""", """            (12u, uint.TryParse(Environment.GetEnvironmentVariable("TERTIUS_PROBE_SIGNAL"), out uint probeSignal) ? probeSignal : 0u),
            // PROBE (ter-pdw8): rayEndVariant of screen_trace.glsl.
            (40u, ScreenTraceHitMipsPass.Variant),
""")
edit('src/Tertius.Rendering/ShaderConstants.cs', """    public const int Count = 13;""", """    public const int Count = 14;""")

# The census's reader: the code holds the bin in its low 6 bits and the guided flag in bit 6.
census = 'src/Tertius.Rendering/Scene/RayCensus.cs'
edit(census, """                int bin = (int)a;
                int kind = bin == 0 ? 0 : bin <= 22 ? 1 : 2;
                double ratio = double.Exp2((a - bin) * 16f);""", """                int code = (int)a; // PROBE (ter-pdw8): bit 6 is the guided flag
                int bin = code & 63;
                int kind = bin == 0 ? 0 : bin <= 22 ? 1 : 2;
                double ratio = double.Exp2((a - code) * 16f);""")
edit(census, """                int bin = (int)a;
                float x = 0.2126f * values[4 * pixel] + 0.7152f * values[4 * pixel + 1] + 0.0722f * values[4 * pixel + 2];
                writer.Write(x);
                writer.Write((float)(x * double.Exp2((a - bin) * 16f)));
                writer.Write((byte)bin);""", """                int code = (int)a; // PROBE (ter-pdw8): the byte holds the bin and, in bit 6, the guided flag
                float x = 0.2126f * values[4 * pixel] + 0.7152f * values[4 * pixel + 1] + 0.0722f * values[4 * pixel + 2];
                writer.Write(x);
                writer.Write((float)(x * double.Exp2((a - code) * 16f)));
                writer.Write((byte)code);""")
print('ray-end edits applied to', root)
