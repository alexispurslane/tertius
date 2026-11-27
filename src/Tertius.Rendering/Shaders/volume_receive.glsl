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
void volumeAt(int level, vec3 p, vec3 n, vec3 reflection, bool specular, out vec4 diffuse, out vec4 glossy)
{
    vec3 local = clamp(p - 0.5, 0.0, 15.0);
    ivec3 base = ivec3(floor(local));
    vec3 f = fract(local);
    diffuse = vec4(0);
    glossy = vec4(0);
    for (int i = 0; i < 8; ++i)
    {
        ivec3 corner = ivec3(i % 2, (i / 2) % 2, i / 4);
        vec3 w = mix(1.0 - f, f, vec3(corner));
        float weight = w.x * w.y * w.z;
        int first = (level * Cells + cellIndex(min(base + corner, ivec3(15)))) * 12;
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
        vec3 edge = min(p, vec3(GridSide) - p);
        float weight = remaining * clamp(min(edge.x, min(edge.y, edge.z)) - 0.5, 0.0, 1.0);
        if (weight == 0.0)
            continue;
        vec3 local = clamp(p + 0.45 * geometricNormal - 0.5, 0.0, 15.0);
        ivec3 base = ivec3(floor(local));
        vec3 f = fract(local);
        float sampleA = 0.0;
        for (int i = 0; i < 8; ++i)
        {
            ivec3 corner = ivec3(i % 2, (i / 2) % 2, i / 4);
            vec3 w = mix(1.0 - f, f, vec3(corner));
            int first = (level * Cells + cellIndex(min(base + corner, ivec3(15)))) * 12;
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
        vec3 edge = min(p, vec3(GridSide) - p);
        float weight = remaining * clamp(min(edge.x, min(edge.y, edge.z)) - 0.5, 0.0, 1.0);
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
