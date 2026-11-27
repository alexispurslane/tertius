#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "vectors.glsl"
#include "gbuffer_inputs.glsl"
#include "lighting.glsl"
#define VOLUME_ACCESS
#include "volume.glsl"
void main()
{
    int side = int(gl_FragCoord.x) % 6;
    int tile = int(gl_FragCoord.x) / 6;
    int level = int(gl_FragCoord.y);
    if (gl_HelperInvocation || level >= Levels || tile >= Tiles)
        return;
    int patchIndex = (level * Tiles + tile) * 6 + side;
    if (frame.counts.x == 0u)
    {
        volume.whiteLight[patchIndex] = vec4(0);
        return;
    }
    int s = patchSide(level), n = GridSide / s;
    if (tile >= n * n * n)
    {
        volume.whiteLight[patchIndex] = vec4(0);
        return;
    }
    ivec3 origin = s * ivec3(tile % n, (tile / n) % n, tile / (n * n));
    vec3 normal = vec3(0), position = vec3(0);
    float sum = 0;
    for (int i = 0; i < s * s * s; ++i)
    {
        ivec3 c = origin + ivec3(i % s, (i / s) % s, i / (s * s));
        int k = (level * Cells + cellIndex(c)) * 6 + side;
        if (all(equal(volume.surfaces[k].color.rgb, uvec3(0))))
            continue;
        ivec4 an = volume.surfaces[k].areaNormal;
        float w = float(an.w);
        normal += vec3(an.xyz);
        position += w * (vec3(c) + 0.5);
        sum += w;
    }
    vec3 light = vec3(0);
    if (sum > 0.0)
    {
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
        light = directLight(s, lobesOf(s, s.normal), s.normal, ShadowMapsHolding, (MainView - 1u)).diffuse;
        atomicAdd(volume.stats.x, 1u);
    }
    volume.whiteLight[patchIndex] = vec4(min(light, vec3(MaxRadiance)), 0);
}
