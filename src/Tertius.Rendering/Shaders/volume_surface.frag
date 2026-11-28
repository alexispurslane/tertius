#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#define VOLUME_ACCESS
#include "volume.glsl"
layout(location = 0) in vec3 world;
layout(location = 1) in vec2 uv;
layout(location = 2) flat in uint axis;
layout(push_constant) uniform VolumeDraw
{
    vec4 row0;
    vec4 row1;
    vec4 row2;
    vec4 grid;
    uvec4 words;
}
draw;
void main()
{
    vec2 uvDx = dFdx(uv), uvDy = dFdy(uv);
    vec3 crossArea = cross(dFdx(world), dFdy(world));
    vec3 a = abs(crossArea);
    uint dominant = a.x >= max(a.y, a.z) ? 0u : a.y >= a.z ? 1u : 2u;
    if (axis != dominant || a[axis] <= 0.0)
        return;
    vec3 n = normalize(crossArea) * (gl_FrontFacing ? -1.0 : 1.0) * (draw.words.w != 0u ? -1.0 : 1.0);
    int side = int(axis) * 2 + int(n[axis] < 0.0);
    int finest = int(draw.words.x);
    if (finest > 0)
    {
        vec4 finer = volume.grids[finest - 1];
        if (withinGrid(finest - 1, ivec3(floor((world - finer.xyz) / finer.w))))
            return;
    }
    // A level with fewer than MaxSide cells covers only part of the raster (volume_surface.vert): leave before the
    // material's reads.
    vec4 g = draw.grid;
    ivec3 c = ivec3(floor((world - g.xyz) / g.w));
    if (!withinGrid(finest, c))
        return;
    GpuMaterial m = materials[draw.words.y];
    vec4 base =
        m.baseColor * textureGrad(sampler2D(textures[nonuniformEXT(m.baseColorMap)], materialSampler), uv, uvDx, uvDy);
    if (base.a < m.alphaCutoff)
        return;
    float metal =
        m.metallic *
        textureGrad(sampler2D(textures[nonuniformEXT(m.metallicRoughnessMap)], materialSampler), uv, uvDx, uvDy).b;
    vec3 diffuse = clamp(base.rgb * (1.0 - metal), 0.0, 1.0);
    vec4 reflectance = capturedReflectance(diffuse);
    vec3 glow = capturedEmission(min(glowSent(m.emissive, draw.words.z), vec3(32256)));
    float area = length(crossArea) / (g.w * g.w) * 4096.0;
    int k = (firstCellOf(finest) + cellIndex(finest, c)) * 6 + side;
    atomicAdd(volume.surfaces[k].areaNormal.w, int(round(area)));
    for (int i = 0; i < 3; ++i)
    {
        atomicAdd(volume.surfaces[k].areaNormal[i], int(round(area * n[i])));
        atomicAdd(volume.surfaces[k].color[i], uint(round(area * reflectance[i])));
        atomicAdd(volume.surfaces[k].source[i], uint(round(area * glow[i] / EmissionScale)));
    }
#if RESTRAINT_VARIANT == 2 || RESTRAINT_VARIANT == 3
    atomicAdd(volume.surfaces[k].color.w, uint(round(area * reflectance.w)));
#endif
}
