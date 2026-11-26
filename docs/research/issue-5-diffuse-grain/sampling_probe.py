"""Temporary source-sampling control using the engine's existing trace and weights."""


def independent_common(source):
    changes = [
        ('uint kind)\n{\n    TracedRay result;', 'uint kind, uint probeSample)\n{\n    TracedRay result;'),
        ('drawOf(pixel, trace.drawIndex, DirectionDraw[kind]);', 'drawOf(pixel, trace.drawIndex, DirectionDraw[kind] ^ (0x9E3779B9u * probeSample));'),
        ('vec2 u = kind == KindReflection ?', 'vec2 u = probeSample > 0u ? reflectionNumbersOf(cell, trace.drawIndex + 31u * probeSample, draw)\n        : kind == KindReflection ?'),
        ('float share = guidedShareOf(p, kind, pixel);', 'float share = probeSample > 0u ? 0.0 : guidedShareOf(p, kind, pixel);'),
        ('drawn = kind == KindReflection ? reflectionDirection(p, u)', 'drawn = kind == KindReflection || probeSample > 0u ? reflectionDirection(p, u)'),
        ('result.direction = kind == KindReflection && share <= 0.0 ?', 'result.direction = (kind == KindReflection || probeSample > 0u) && share <= 0.0 ?'),
        ('result.lobeDensity = densityOf(p, kind, result.direction);', 'result.lobeDensity = probeSample > 0u ? reflectionDensity(p, result.direction) : densityOf(p, kind, result.direction);'),
    ]
    for before, after in changes:
        assert source.count(before) == 1, before
        source = source.replace(before, after)
    marker = '// The share of the diffuse ray\'s estimate in the pixel\'s specular sample:'
    wrapper = '''// Research: standard callers keep their original draw, density and source light.
TracedRay traceFrom(ivec2 pixel, ivec2 cell, TracingPixel p, uint kind)
{
    return traceFrom(pixel, cell, p, kind, 0u);
}

'''
    assert source.count(marker) == 1
    return source.replace(marker, wrapper + marker)


def independent_resolve(source, samples):
    start = source.index('    if (handOver > 0.0 && diffuseRay.brings)')
    end = source.index('    if (startsKind(p, KindReflection))', start)
    replacement = '''    float hitDistance = diffuse.light.a;
    if (handOver > 0.0)
    {
        hitDistance = 0.0;
        for (uint probeSample = 1u; probeSample <= PROBE_SAMPLES; probeSample++)
        {
            // Same diffuse-kind hit and miss light and walk budget; a new GGX direction and its density.
            RayRecord probeRecord = recordOf(traceFrom(pixel, pixel, p, KindDiffuse, probeSample));
            WeighedRay probeRay = weighedRayOf(p, probeRecord, vec4(0.0), KindDiffuse, plan);
            hitDistance += probeRecord.light.a / float(PROBE_SAMPLES);
            if (!probeRay.brings)
                continue;
            specularLight += handOver * probeRecord.light.rgb
                * specularWeightOf(p, lobes, factors, probeRay) / float(PROBE_SAMPLES);
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
            uint probeCode = ageCodeOfRecord(probeRecord.ray);
            if (probeCode != 0u)
                ageCode = ageCode == 0u ? probeCode : min(ageCode, probeCode);
            moved = moved || movedHitOfRecord(probeRecord.ray);
#endif
#ifdef TERTIUS_LIGHT_AGE_DIAGNOSTIC
            uint probeSeen = seenCodeOfRecord(probeRecord.ray);
            if (probeSeen != 0u)
                seenCode = seenCode == 0u ? probeSeen : min(seenCode, probeSeen);
#endif
        }
    }
'''.replace('PROBE_SAMPLES', str(samples) + 'u')
    return source[:start] + replacement + source[end:]
