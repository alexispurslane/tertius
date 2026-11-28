#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "vectors.glsl"
#include "gbuffer_inputs.glsl"
// PROTOTYPE (issue #42): shapeLight also sums what gray paint keeps of each lamp alone, in the specular words.
#define VOLUME_JUDGES_LAMPS
#include "lighting.glsl"
#define VOLUME_ACCESS
#include "volume.glsl"

// The direct light on a unit white diffuse surface in `diffuse`, and in `specular` the sum over the lamps of what gray
// paint keeps of each (lampGrayKeeps).
DirectTerms whiteLightAt(int level, int side, ivec3 origin)
{
    DirectTerms none;
    none.diffuse = vec3(0);
    none.specular = vec3(0);
    if (frame.counts.x == 0u)
        return none;
    vec3 normal = vec3(0), position = vec3(0);
    float sum = 0;
    for (int i = 0; i < 64; ++i)
    {
        ivec3 c = origin + ivec3(i % 4, (i / 4) % 4, i / 16);
        // A group of the outer grid's guard shell holds only the cells within the grid.
        if (!withinGrid(level, c))
            continue;
        int k = bucketIndex(level, c, side);
        if (all(equal(volume.surfaces[k].color, uvec4(0))))
            continue;
        ivec4 an = volume.surfaces[k].areaNormal;
        float w = float(an.w);
        normal += vec3(an.xyz);
        position += w * (vec3(c) + 0.5);
        sum += w;
    }
    if (sum <= 0.0)
        return none;

    vec4 g = volume.grids[level];
    ShadingInput s;
    s.position = g.xyz + (position / sum) * g.w;
    s.normal = normalizedOr(normal, axisNormal(side));
    s.geometricNormal = s.normal;
    s.metallic = 0;
    s.baseColor = vec3(1);
    s.roughness = 1;
    s.reflectance = 0;
    s.strand = vec3(0);
    s.model = ShadingStandard;
    s.position += 0.5 * g.w * s.normal;
    atomicAdd(volume.stats.x, 1u);
    DirectTerms light = directLight(s, lobesOf(s, s.normal), s.normal, ShadowMapsHolding, (MainView - 1u));
#if VOLUME_COUNTERS
    atomicAdd(volume.surfaces[2].source.w, uint(volumeVisibleLamps));
    atomicMax(volume.surfaces[3].source.w, uint(volumeVisibleLamps));
#endif
    light.diffuse = min(light.diffuse, vec3(MaxRadiance));
    light.specular = min(light.specular, vec3(MaxRadiance));
    return light;
}

void main()
{
    int side = int(gl_FragCoord.x) % 6;
    int tile = int(gl_FragCoord.x) / 6;
    int level = int(gl_FragCoord.y);
    if (gl_HelperInvocation || level >= Levels || tile >= tilesOf(level))
        return;
    ivec3 origin = tileOrigin(level, tile);
    DirectTerms direct = whiteLightAt(level, side, origin);
    vec3 light = direct.diffuse;
#if RESTRAINT_VARIANT == 3 && RESTRAINT_LAMPS == 1
    vec3 grayKeeps = direct.specular;
#else
    vec3 grayKeeps = grayPaintKeeps(light, COLOR_GAIN);
#endif
    for (int i = 0; i < 64; ++i)
    {
        ivec3 c = origin + ivec3(i % 4, (i / 4) % 4, i / 16);
        if (!withinGrid(level, c))
            continue;
        int k = bucketIndex(level, c, side);
        float area = float(volume.surfaces[k].areaNormal.w);
        if (area <= 0.0)
            continue;
        vec3 emission = vec3(volume.surfaces[k].source.rgb) * EmissionScale;
#if RESTRAINT_VARIANT == 5
        // The product rule for each lamp of the group, on the cell face's mean paint.
        vec3 reflected = vec3(0);
        for (int lamp = 0; lamp < volumeLampCount; ++lamp)
            reflected += reflectedLight(vec4(volume.surfaces[k].color), area, min(volumeLamps[lamp], vec3(MaxRadiance)), grayKeeps, COLOR_GAIN);
#else
        vec3 reflected = reflectedLight(vec4(volume.surfaces[k].color), area, light, grayKeeps, COLOR_GAIN);
#endif
        volume.surfaces[k].source.rgb = floatBitsToUint(emission + reflected);
    }
}
