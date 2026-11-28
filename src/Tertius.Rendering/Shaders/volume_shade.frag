#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "vectors.glsl"
#include "gbuffer_inputs.glsl"
#define VOLUME_ACCESS
#include "volume.glsl"
// shapeLight also sums what gray paint keeps of each lamp alone, into volumeGrayPaintKeeps (volume.glsl, the restraint).
#define VOLUME_JUDGES_LAMPS
#include "lighting.glsl"

// The direct light of one lighting group: `total` falls on a unit white diffuse surface, and gray paint keeps
// `grayKept` of it, judged lamp by lamp (grayPaintKeeps).
struct GroupLight
{
    vec3 total;
    vec3 grayKept;
};

GroupLight whiteLightAt(int level, int side, ivec3 origin)
{
    GroupLight none = GroupLight(vec3(0), vec3(0));
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
    return GroupLight(min(light.diffuse, vec3(MaxRadiance)), min(volumeGrayPaintKeeps, vec3(MaxRadiance)));
}

void main()
{
    int side = int(gl_FragCoord.x) % 6;
    int tile = int(gl_FragCoord.x) / 6;
    int level = int(gl_FragCoord.y);
    if (gl_HelperInvocation || level >= Levels || tile >= tilesOf(level))
        return;
    ivec3 origin = tileOrigin(level, tile);
    GroupLight light = whiteLightAt(level, side, origin);
    for (int i = 0; i < 64; ++i)
    {
        ivec3 c = origin + ivec3(i % 4, (i / 4) % 4, i / 16);
        if (!withinGrid(level, c))
            continue;
        int k = bucketIndex(level, c, side);
        if (volume.surfaces[k].areaNormal.w <= 0)
            continue;
        vec3 emission = vec3(volume.surfaces[k].source.rgb) * EmissionScale;
        vec3 reflected = reflectedLight(volume.surfaces[k].color, light.total, light.grayKept);
        volume.surfaces[k].source.rgb = floatBitsToUint(emission + reflected);
    }
}
