// The directional light volume's buffer (volume_layout.glsl has the grids' shape).
// Each signed dominant-normal bucket sums geometry in units of 1/4096 of one cell face.
#include "volume_layout.glsl"
const float EmissionScale = 4.0;
const float NeutralSourceShare = 0.02;
struct SurfaceBucket
{
    ivec4 areaNormal; // xyz oriented area vector, w area
    uvec4 color;      // rgb area times diffuse reflectance
    // Rasterization accumulates area times emission as fixed-point integers.
    // Shading replaces these words with area times total outgoing radiance in float bits.
    uvec4 source;
};
#ifndef VOLUME_SET
#define VOLUME_SET 3
#endif
#ifndef VOLUME_BINDING
#define VOLUME_BINDING 0
#endif
layout(set = VOLUME_SET, binding = VOLUME_BINDING, std430) VOLUME_ACCESS buffer LightGrid
{
    vec4 grids[Levels]; // lowest corner relative to the camera (m), cell width (m)
    uvec4 stats;        // x lighting evaluations
    SurfaceBucket surfaces[CellCount * 6];
    uvec4 field[CellCount * Directions]; // outgoing radiance and remaining environment transmission
    vec4 bounce[CellCount * 6];
    vec4 filtered[CellCount * 12]; // directional diffuse and broad specular integrals
}
volume;
ivec3 directionStep(int d)
{
    // Four directions per octant: one diagonal, three tilted toward the axes.
    // No quadrature point represents an infinitely narrow axial beam.
    int octant = d / 4;
    ivec3 v = ivec3(1);
    int major = d % 4;
    if (major < 3)
        v[major] = 3;
    return v * (ivec3(1) - 2 * ivec3(octant & 1, (octant >> 1) & 1, octant >> 2));
}
vec3 axisNormal(int side)
{
    vec3 n = vec3(0);
    n[side / 2] = (side % 2 == 0) ? 1.0 : -1.0;
    return n;
}

vec4 outgoingOf(uvec4 pair)
{
    return vec4(unpackHalf2x16(pair.x).y, unpackHalf2x16(pair.y).y, unpackHalf2x16(pair.z).y, unpackHalf2x16(pair.w).y);
}
vec4 incomingOf(uvec4 pair)
{
    return vec4(unpackHalf2x16(pair.x).x, unpackHalf2x16(pair.y).x, unpackHalf2x16(pair.z).x, unpackHalf2x16(pair.w).x);
}

vec4 parentOutgoing(ivec3 c, int level, int direction)
{
    if (level + 1 == Levels)
        return vec4(0, 0, 0, 1);
    vec4 grid = volume.grids[level], larger = volume.grids[level + 1];
    vec3 world = grid.xyz + (vec3(c) + 0.5) * grid.w;
    ivec3 coarse = clamp(ivec3(floor((world - larger.xyz) / larger.w)), 0, sideOf(level + 1) - 1);
    return outgoingOf(volume.field[fieldIndex(level + 1, direction, cellIndex(level + 1, coarse))]);
}

// Low-saturation indirect sources are restrained to NeutralSourceShare so neutral bounce
// preserves contrast while saturated colors keep their full indirect strength. Scaling all
// channels by a function of saturation alone preserves hue and keeps the light field
// independent of camera exposure.
vec3 restrainNeutral(vec3 radiance)
{
    float peak = max(radiance.r, max(radiance.g, radiance.b));
    float valley = min(radiance.r, min(radiance.g, radiance.b));
    float chroma = smoothstep(0.58, 0.78, (peak - valley) / max(peak, 1e-12));
    return radiance * mix(NeutralSourceShare, 1.0, chroma);
}
