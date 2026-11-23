# Tool code of ter-pdw8, the second probe step, applied after apply-rayend.py on the same build copy (never to src).
# It answers the line orchestrator's two cause questions on the lamp hall:
#   - V1 becomes stand-in (c): the running mean covers the direct part of the hit light alone (this draw's direct light
#     times the material factors, plus the glow), and the history part stays this draw's, as today. Layers 4 and 5 hold
#     the direct mean's state (rgb = S / C, alpha = C, as the old stand-in), layers 6 and 7 the value that a hit reads,
#     S + this draw's history part, with alpha = this draw's coverage, so that the mip atlas treats them as layers 0 and 1.
#   - Bit 2 keeps the old stand-in (the whole hit light's running mean, layers 2 and 3) for the settling test.
#   - Bit 3: the mip atlas means only the children that hold light, so that a lamp (which sends no glow) and the sky do
#     not dilute a cell.
#   - The census records each sample's hit pixel and the level of the footprint formula (whatever the variant reads):
#     layers 8 to 11 carry it from the block and own rays to the resolve, layers 12 and 13 hold the pixel's diffuse and
#     reflection hit (x, y, level, 1; -1 and alpha 0 for none). RayCensus counts the level of every far diffuse hit and
#     every reflection hit per pixel (<prefix>.levels) and writes the hits of the kept samples (<prefix>.hits<k>).
# Variant values: 0 V0, 1 V1 (c), 2 V2, 3 V3 = (c) + V2, 4 the old stand-in, 10 V2 over covered children, 11 V3 so.
# Usage: python3 apply-rayend2.py <build copy root>
import sys
from pathlib import Path

root = Path(sys.argv[1])


def edit(rel, old, new, count=1):
    path = root / rel
    text = path.read_text()
    assert text.count(old) == count, (rel, old[:80], text.count(old))
    path.write_text(text.replace(old, new))


S = 'src/Tertius.Rendering/Shaders/'

# The read: the layer and the level.
edit(S + 'screen_trace.glsl',
     '''// PROBE (ter-pdw8, never commit to src): what a ray's end reads. Bit 0: the running mean of the hit light (layers 2 and
// 3) in place of this draw's (layers 0 and 1). Bit 1: the level of the hit light's mip atlas that matches the ray's
// footprint at the hit, in place of the hit pixel.''',
     '''// PROBE (ter-pdw8, never commit to src): what a ray's end reads. Bit 0: stand-in (c), the running mean of the direct
// part plus this draw's history part (layers 6 and 7), in place of this draw's hit light (layers 0 and 1). Bit 1: the
// level of the hit light's mip atlas that matches the ray's footprint at the hit, in place of the hit pixel. Bit 2: the
// old stand-in, the running mean of the whole hit light (layers 2 and 3). Bit 3: the atlas means only children that
// hold light.''')
edit(S + 'screen_trace.glsl',
     '''vec3 probeHitLightOf(ivec2 q, uint kind, float footprint)
{
    int layer = int(kind) + ((rayEndVariant & 1u) != 0u ? 2 : 0);
    int level = 0;
    if ((rayEndVariant & 2u) != 0u)
        level = clamp(int(floor(log2(max(footprint, 1.0)) + 0.5)), 0, probeTopLevel());
    vec4 value = imageLoad(hitLight, ivec3(probeAtlasOf(level, q >> level), layer));
    // The mean layers hold the light where covered and the coverage: their light over the draws is the product.
    return (rayEndVariant & 1u) != 0u ? value.rgb * value.a : value.rgb;
}''',
     '''int probeLevelOf(float footprint)
{
    return clamp(int(floor(log2(max(footprint, 1.0)) + 0.5)), 0, probeTopLevel());
}

vec3 probeHitLightOf(ivec2 q, uint kind, float footprint)
{
    bool old = (rayEndVariant & 4u) != 0u;
    int layer = int(kind) + (old ? 2 : (rayEndVariant & 1u) != 0u ? 6 : 0);
    int level = (rayEndVariant & 2u) != 0u ? probeLevelOf(footprint) : 0;
    vec4 value = imageLoad(hitLight, ivec3(probeAtlasOf(level, q >> level), layer));
    // The old stand-in's layers hold the light where covered and the coverage: their light over the draws is the product.
    return old ? value.rgb * value.a : value.rgb;
}''')
edit(S + 'screen_trace.glsl',
     '''    bool probeGuided; // PROBE (ter-pdw8): the ray took the guided draw''',
     '''    bool probeGuided; // PROBE (ter-pdw8): the ray took the guided draw
    vec4 probeHit;    // PROBE (ter-pdw8): the hit pixel, the formula's level and 1; -1 and 0 without a read hit''')
edit(S + 'screen_trace.glsl',
     '''    result.probeGuided = guided;''',
     '''    result.probeGuided = guided;
    result.probeHit = vec4(-1.0, -1.0, -1.0, 0.0);''')
edit(S + 'screen_trace.glsl',
     '''    result.light = mix(
        missLight,''',
     '''    result.probeHit = vec4(vec2(hitPixel), float(probeLevelOf(probeFootprint)), 1.0);
    result.light = mix(
        missLight,''')

# The block and own rays keep their hits beside their records.
edit(S + 'screen_trace_block_rays.comp',
     '''    RayRecord record = chosenPixelOf(block, rayKind, chosen, p) ? recordOf(traceFrom(chosen, block, p, rayKind)) : noRay();
    imageStore(blockLight, ivec3(block, rayKind), record.light);''',
     '''    RayRecord record = noRay();
    vec4 probeHit = vec4(-1.0, -1.0, -1.0, 0.0); // PROBE (ter-pdw8)
    if (chosenPixelOf(block, rayKind, chosen, p))
    {
        TracedRay traced = traceFrom(chosen, block, p, rayKind);
        record = recordOf(traced);
        probeHit = traced.probeHit;
    }
    imageStore(hitLight, ivec3(block, 8 + int(rayKind)), probeHit);
    imageStore(blockLight, ivec3(block, rayKind), record.light);''')
edit(S + 'screen_trace_own_rays.comp',
     '''        RayRecord record = recordOf(traceFrom(pixel, pixel, tracingPixelAt(pixel), rayKind));''',
     '''        TracedRay traced = traceFrom(pixel, pixel, tracingPixelAt(pixel), rayKind);
        RayRecord record = recordOf(traced);
        imageStore(hitLight, ivec3(pixel, 10 + int(rayKind)), traced.probeHit); // PROBE (ter-pdw8)''')

# The resolve copies the hit of the record that each sample takes.
edit(S + 'screen_trace_resolve.comp',
     '''// A ray of the record that the pixel weighs''',
     '''// PROBE (ter-pdw8): the hit of the record that recordAt reads.
vec4 probeHitAt(ivec2 pixel, uint kind, uint plan)
{
    bool own = (plan & (kind == KindReflection ? PlanOwnReflection : PlanOwnDiffuse)) != 0u;
    return imageLoad(hitLight, ivec3(own ? pixel : pixel / 2, (own ? 10 : 8) + int(kind)));
}

// A ray of the record that the pixel weighs''')
edit(S + 'screen_trace_resolve.comp',
     '''        imageStore(diffuseSample, pixel, vec4(0.0, 0.0, 0.0, -1.0));''',
     '''        imageStore(diffuseSample, pixel, vec4(0.0, 0.0, 0.0, -1.0));
        imageStore(hitLight, ivec3(pixel, 12), vec4(-1.0, -1.0, -1.0, 0.0)); // PROBE (ter-pdw8)
        imageStore(hitLight, ivec3(pixel, 13), vec4(-1.0, -1.0, -1.0, 0.0));''')
edit(S + 'screen_trace_resolve.comp',
     '''    WeighedRay diffuseRay = weighedRayOf(p, diffuse, KindDiffuse, plan);''',
     '''    WeighedRay diffuseRay = weighedRayOf(p, diffuse, KindDiffuse, plan);
    // PROBE (ter-pdw8): the diffuse sample's hit, none where the ray brings no light.
    imageStore(hitLight, ivec3(pixel, 12), diffuseRay.brings ? probeHitAt(pixel, KindDiffuse, plan) : vec4(-1.0, -1.0, -1.0, 0.0));
    vec4 probeReflectionHit = vec4(-1.0, -1.0, -1.0, 0.0);''')
edit(S + 'screen_trace_resolve.comp',
     '''        WeighedRay reflectionRay = weighedRayOf(p, reflection, KindReflection, plan);
        if (reflectionRay.brings)
        {''',
     '''        WeighedRay reflectionRay = weighedRayOf(p, reflection, KindReflection, plan);
        if (reflectionRay.brings)
        {
            probeReflectionHit = probeHitAt(pixel, KindReflection, plan); // PROBE (ter-pdw8)''')
edit(S + 'screen_trace_resolve.comp',
     '''    imageStore(specularSample, pixel, vec4(min(specularLight, vec3(MaxRadiance)), hitDistance));''',
     '''    imageStore(specularSample, pixel, vec4(min(specularLight, vec3(MaxRadiance)), hitDistance));
    imageStore(hitLight, ivec3(pixel, 13), probeReflectionHit); // PROBE (ter-pdw8)''')

# The plan: the direct part's mean and the value of stand-in (c).
P = S + 'screen_trace_plan.comp'
edit(P,
     '''vec3 probeReflection;
vec3 probeDiffuse;
float probeCovered;''',
     '''vec3 probeReflection;
vec3 probeDiffuse;
float probeCovered;
vec3 probeDirectReflection; // the direct part: this draw's direct light times the material factors, plus the glow
vec3 probeDirectDiffuse;''')
edit(P,
     '''        storeHitLight(pixel, leavingLightOf(pixel, p.s, p.V, r));''',
     '''        storeHitLight(pixel, leavingLightOf(pixel, p.s, p.V, r));
        {   // PROBE (ter-pdw8): the direct part, as leavingLightOf adds it.
            MaterialFactors factors = materialFactors(p.s, lobesOf(p.s, p.V));
            probeDirectDiffuse = min(factors.diffuse * imageLoad(directDiffuseLight, pixel).rgb + glowAt(pixel), vec3(MaxRadiance));
            probeDirectReflection = min(probeDirectDiffuse + factors.specular * imageLoad(directSpecularLight, pixel).rgb, vec3(MaxRadiance));
        }''')
edit(P,
     '''        storeHitLight(pixel, LeavingLight(glowAt(pixel), glowAt(pixel)));''',
     '''    {
        storeHitLight(pixel, LeavingLight(glowAt(pixel), glowAt(pixel)));
        probeDirectDiffuse = glowAt(pixel); // PROBE (ter-pdw8): an impostor's light is all direct
        probeDirectReflection = glowAt(pixel);
    }''')
edit(P,
     '''    probeCovered = 0.0;
#ifdef''',
     '''    probeCovered = 0.0;
    probeDirectReflection = vec3(0.0);
    probeDirectDiffuse = vec3(0.0);
#ifdef''')
edit(P,
     '''        imageStore(hitLight, ivec3(pixel, 2 + kind), vec4(covered > 0.0 ? sum / covered : vec3(0.0), covered));
    }
}''',
     '''        imageStore(hitLight, ivec3(pixel, 2 + kind), vec4(covered > 0.0 ? sum / covered : vec3(0.0), covered));
        // Stand-in (c): the same running mean of the direct part alone (layers 4 and 5), and the value that a hit reads,
        // the direct mean plus this draw's history part (layers 6 and 7, alpha this draw's coverage).
        vec3 direct = (kind == 0 ? probeDirectReflection : probeDirectDiffuse) * probeCovered;
        vec3 history = max((kind == 0 ? probeReflection : probeDiffuse) * probeCovered - direct, vec3(0.0));
        last = imageLoad(hitLight, ivec3(pixel, 4 + kind));
        sum = last.rgb * last.a;
        covered = last.a;
        if (trace.drawIndex == 0u || !(covered >= 0.0 && covered <= 1.0))
        {
            sum = direct;
            covered = probeCovered;
        }
        else
        {
            sum += (direct - sum) / count;
            covered += (probeCovered - covered) / count;
        }
        imageStore(hitLight, ivec3(pixel, 4 + kind), vec4(covered > 0.0 ? sum / covered : vec3(0.0), covered));
        imageStore(hitLight, ivec3(pixel, 6 + kind), vec4(probeCovered > 0.0 ? sum + history : vec3(0.0), probeCovered));
    }
}''')

# The atlas: eight layers, and bit 3 skips the children without light.
M = S + 'screen_trace_hit_mips.comp'
edit(M, '''    for (int layer = 0; layer < 4; layer++)''', '''    for (int layer = 0; layer < 8; layer++)''')
edit(M,
     '''                vec4 value = imageLoad(hitLight, ivec3(probeAtlasOf(level - 1, child), layer));''',
     '''                vec4 value = imageLoad(hitLight, ivec3(probeAtlasOf(level - 1, child), layer));
                if ((rayEndVariant & 8u) != 0u && all(equal(value.rgb, vec3(0.0))))
                    value.a = 0.0;''')

edit('src/Tertius.Rendering/Frame/ScreenTraceImages.cs',
     '''            layers: 4,
            name: "screen trace hit light"''',
     '''            layers: 14, // PROBE (ter-pdw8): 4 to 7 stand-in (c), 8 to 13 the rays' hits
            name: "screen trace hit light"''')

# The census: the level histograms and the kept samples' hits.
C = 'src/Tertius.Rendering/Scene/RayCensus.cs'
edit(C,
     '''    private static BinaryWriter[]? keepWriters;''',
     '''    private static BinaryWriter[]? keepWriters;
    private static BinaryWriter[]? hitWriters; // PROBE (ter-pdw8)
    private static float[][]? levels; // PROBE (ter-pdw8): 16 levels of the far diffuse hits, then 16 of the reflection hits''')
edit(C,
     '''            keepWriters = [.. Keeps.Select((_, k) => new BinaryWriter(File.Create($"{Prefix}.keep{k}")))];
        }''',
     '''            keepWriters = [.. Keeps.Select((_, k) => new BinaryWriter(File.Create($"{Prefix}.keep{k}")))];
            hitWriters = [.. Keeps.Select((_, k) => new BinaryWriter(File.Create($"{Prefix}.hits{k}")))];
            levels = [.. Enumerable.Range(0, 32).Select(_ => new float[width * height])];
        }
        (float[] hd, int hw, _) = renderer.CensusReadLevel(renderer.ScreenTraceImages.HitLight, 12, 0);
        (float[] hr, _, _) = renderer.CensusReadLevel(renderer.ScreenTraceImages.HitLight, 13, 0);
        float[][] lv = levels!;
        Parallel.For(0, height, row =>
        {
            for (int column = 0; column < width; column++)
            {
                int pixel = row * width + column;
                int at = 4 * (row * hw + column);
                float a = values[4 * pixel + 3];
                if (a >= 0f && ((int)a & 63) >= 23 && hd[at + 3] > 0.5f)
                    lv[Math.Clamp((int)hd[at + 2], 0, 15)][pixel] += 1f;
                if (hr[at + 3] > 0.5f)
                    lv[16 + Math.Clamp((int)hr[at + 2], 0, 15)][pixel] += 1f;
            }
        });''')
edit(C,
     '''                writer.Write(specular[4 * pixel + 3]);
                count++;''',
     '''                writer.Write(specular[4 * pixel + 3]);
                int hat = 4 * (row * hw + column);
                foreach (float v in new float[] { hd[hat], hd[hat + 1], hd[hat + 2], hr[hat], hr[hat + 1], hr[hat + 2] })
                    hitWriters![k].Write(v);
                count++;''')
edit(C,
     '''        foreach (BinaryWriter keep in keepWriters!)
            keep.Dispose();''',
     '''        foreach (BinaryWriter keep in keepWriters!)
            keep.Dispose();
        foreach (BinaryWriter hits in hitWriters!)
            hits.Dispose();
        using (var writer = new BinaryWriter(File.Create($"{Prefix}.levels")))
            foreach (float[] array in levels!)
                foreach (float value in array)
                    writer.Write(value);''')
print('applied')
