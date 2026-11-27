#ifndef TERTIUS_VOLUME_RECEIVE_GLSL
#define TERTIUS_VOLUME_RECEIVE_GLSL

#ifndef VOLUME_BINDING
#define VOLUME_BINDING 5
#endif
#define VOLUME_ACCESS readonly
#include "volume.glsl"
// The positive and negative axial lobes are weighted continuously on the unit sphere.
vec4 volumeLobes(int first, vec3 n)
{
    vec3 weights = n * n;
    return weights.x * volume.filtered[first + int(n.x < 0.0)] +
           weights.y * volume.filtered[first + 2 + int(n.y < 0.0)] +
           weights.z * volume.filtered[first + 4 + int(n.z < 0.0)];
}
// The share of a receiver's light that a level gives before the coarser levels give the rest. The level's reach is a
// cube of Reach cells per axis. The share falls from 1 to 0 over the cell that ends half a cell inside the reach's face,
// by the receiver's largest distance along the camera frame's axes from the reach's center. That center follows the
// camera as far as the guard lets it. An inner grid has no guard, so its reach is the grid and moves with it in whole
// cells. The outer grid's guard of one cell is wider than the camera's distance from the grid's center, at most half a
// cell, so its reach is centered on the camera and moves continuously, and its fade ends 7.5 cells from the camera. A
// receiver there reads cells up to 0.45 cells farther out, by its normal offset: within the stored cell centers, which
// reach at least 8 cells from the camera along each axis. `p` is the receiver's place in the grid's cells, and the
// camera stands at -grid.xyz / grid.w.
float levelWeight(int level, vec4 grid, vec3 p)
{
    float side = float(sideOf(level));
    vec3 gridCenter = vec3(0.5 * side);
    vec3 center = clamp(-grid.xyz / grid.w, gridCenter - float(guardOf(level)), gridCenter + float(guardOf(level)));
    vec3 offset = abs(p - center);
    return clamp(0.5 * float(Reach) - 0.5 - max(offset.x, max(offset.y, offset.z)), 0.0, 1.0);
}
void volumeAt(int level, vec3 p, vec3 n, vec3 reflection, bool specular, out vec4 diffuse, out vec4 glossy)
{
    int last = sideOf(level) - 1;
    vec3 local = clamp(p - 0.5, 0.0, float(last));
    ivec3 base = ivec3(floor(local));
    vec3 f = fract(local);
    diffuse = vec4(0);
    glossy = vec4(0);
    for (int i = 0; i < 8; ++i)
    {
        ivec3 corner = ivec3(i % 2, (i / 2) % 2, i / 4);
        vec3 w = mix(1.0 - f, f, vec3(corner));
        float weight = w.x * w.y * w.z;
        int first = (firstCellOf(level) + cellIndex(level, min(base + corner, ivec3(last)))) * 12;
        diffuse += weight * volumeLobes(first, n);
        if (specular)
            glossy += weight * volumeLobes(first + 6, reflection);
    }
}
// Remaining environment transmission along `direction` at `position`, offset along `geometricNormal`.
float volumeTransmissionAt(vec3 position, vec3 geometricNormal, vec3 direction)
{
    float transmission = 1.0;
    float remaining = 1.0;
    for (int level = 0; level < Levels && remaining > 0.0; ++level)
    {
        vec4 grid = volume.grids[level];
        vec3 p = (position - grid.xyz) / grid.w;
        float weight = remaining * levelWeight(level, grid, p);
        if (weight == 0.0)
            continue;
        int last = sideOf(level) - 1;
        vec3 local = clamp(p + 0.45 * geometricNormal - 0.5, 0.0, float(last));
        ivec3 base = ivec3(floor(local));
        vec3 f = fract(local);
        float sampleA = 0.0;
        for (int i = 0; i < 8; ++i)
        {
            ivec3 corner = ivec3(i % 2, (i / 2) % 2, i / 4);
            vec3 w = mix(1.0 - f, f, vec3(corner));
            int first = (firstCellOf(level) + cellIndex(level, min(base + corner, ivec3(last)))) * 12;
            sampleA += w.x * w.y * w.z * volumeLobes(first + 6, direction).a;
        }
        transmission += weight * (sampleA - 1.0);
        remaining -= weight;
    }
    return clamp(transmission, 0.0, 1.0);
}
void applyLightVolume(
    ShadingInput s, vec3 view, MaterialFactors factors, bool replaceGlossy, inout vec3 diffuse, inout vec3 specular)
{
    vec4 d = vec4(0, 0, 0, 1), g = d;
    vec3 reflected = normalize(reflect(-view, s.normal));
    float share = glossyShare(s.roughness);
    bool needsSpecular = share < 1.0 || !replaceGlossy;
    float remaining = 1.0;
    for (int level = 0; level < Levels && remaining > 0.0; ++level)
    {
        vec4 grid = volume.grids[level];
        vec3 p = (s.position - grid.xyz) / grid.w;
        float weight = remaining * levelWeight(level, grid, p);
        if (weight == 0.0)
            continue;
        vec4 ld, lg;
        volumeAt(level, p + 0.45 * s.geometricNormal, s.normal, reflected, needsSpecular, ld, lg);
        d += weight * (ld - vec4(0, 0, 0, 1));
        g += weight * (lg - vec4(0, 0, 0, 1));
        remaining -= weight;
    }
    diffuse = clamp(d.a, 0.0, 1.0) * diffuse + factors.diffuse * preExposed(max(d.rgb, vec3(0)));
    if (needsSpecular)
    {
        float open = clamp(g.a, 0.0, 1.0);
        vec3 broad = open * specular + factors.specular * preExposed(max(g.rgb, vec3(0)));
        // When glossy reflections will not replace this specular term, keep volume transmission without coarse volume radiance.
        specular = replaceGlossy ? broad : mix(broad, open * specular, share);
    }
}

#endif
