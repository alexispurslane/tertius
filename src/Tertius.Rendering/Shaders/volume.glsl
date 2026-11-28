// The directional light volume's buffer (volume_layout.glsl has the grids' shape).
// Each signed dominant-normal bucket sums geometry in units of 1/4096 of one cell face.
#include "volume_layout.glsl"
const float EmissionScale = 4.0;
const float NeutralSourceShare = 0.02;
struct SurfaceBucket
{
    ivec4 areaNormal; // xyz oriented area vector, w area
    uvec4 color;      // rgb area times diffuse reflectance; variants 2 and 3 (PROTOTYPE, below): its colored part, w its gray level
    // Rasterization accumulates area times emission as fixed-point integers.
    // Shading replaces these words with area times total outgoing radiance in float bits, in every bucket with area.
    // A raster write adds at least the 256 units of one sample's projected area, so a bucket with nonzero emission
    // words has area, and the restriction and the source pass read only float bits.
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

// The knee of the restraint: 0 for a saturation, (max - min) / max, under 0.58, 1 above 0.78.
float saturationKnee(vec3 c)
{
    float peak = max(c.r, max(c.g, c.b));
    float valley = min(c.r, min(c.g, c.b));
    return smoothstep(0.58, 0.78, (peak - valley) / max(peak, 1e-12));
}

// Low-saturation indirect sources are restrained to NeutralSourceShare so neutral bounce
// preserves contrast while saturated colors keep their full indirect strength. Scaling all
// channels by a function of saturation alone preserves hue and keeps the light field
// independent of camera exposure.
vec3 restrainNeutral(vec3 radiance)
{
    return radiance * mix(NeutralSourceShare, 1.0, saturationKnee(radiance));
}

// PROTOTYPE (issue #42, stage 1). This switch selects the restraint variant for the pictures of the user's choice.
// Stage 2 keeps the chosen rule and removes the switches and the other variants.
//   0: today (eff66e481): the restraint reads each cell face's summed color, and restrains each surface's glow.
//   1: free. Design 1 (the paint decides) was dropped by the user's decision of 2026-11-28.
//   2: Design 2, only gray on gray is held back: the capture splits each reflectance into a colored part (color.rgb)
//      and a gray level (color.w), and the bounce keeps the neutral share of gray level times the light's gray level.
//   3: the weighted split: Design 2's split, where the colored part of the paint and of the light each keeps the weight
//      mix(share, 1, ramp(its own saturation)). Nothing moves from a colored part into a gray level, so a faint tint
//      gives a dim light of the same hue. RESTRAINT_RAMP selects the ramp, and RESTRAINT_LAMPS whether the first
//      bounce judges the summed direct light or each lamp alone.
//   4: today's restraint of scattered light, with the glow unrestrained.
// Variants 2 to 4 send each surface's glow into the volume in full (the user's decision). After the capture, variants
// 2 and 3 only add sums and multiply them by the light. (The old hybrid of variant 3, which moved the dropped colored
// part into the gray level, was rejected by the user on 2026-11-28.)
#ifndef RESTRAINT_VARIANT
#define RESTRAINT_VARIANT 0
#endif
#if RESTRAINT_VARIANT == 1 || RESTRAINT_VARIANT < 0 || RESTRAINT_VARIANT > 4
#error RESTRAINT_VARIANT names no variant
#endif
// PROTOTYPE (issue #42): the ramp of variant 3, of a saturation s from 0 to 1.
//   0 (A): smoothstep(0.58, 0.78, s), today's knee.  1 (B): (s / 0.78)^3, at most 1.  2 (C): smoothstep(0, 0.78, s).
#ifndef RESTRAINT_RAMP
#define RESTRAINT_RAMP 0
#endif
// PROTOTYPE (issue #42): 1 makes variant 3's first bounce judge each lamp's light alone (lighting.glsl, shapeLight,
// with VOLUME_JUDGES_LAMPS from volume_shade.frag). 0 judges the summed direct light of the lighting group.
#ifndef RESTRAINT_LAMPS
#define RESTRAINT_LAMPS 0
#endif
// PROTOTYPE (issue #42, stage 1): the color gain multiplies the part of the first bounce that variants 2 and 3 keep of
// the colored parts. It does not act on emission, and the second bounce carries the boosted light on with no second
// gain.
#ifndef COLOR_GAIN
#define COLOR_GAIN 1.0
#endif
// Variants 2 and 3 store small products, such as area x 0.02 x reflectance, in the integer color words. One fragment
// adds up to about 256 area units, so 64 steps per unit keep about 200 steps for one fragment of 0.65 gray paint at
// the neutral share.
#if RESTRAINT_VARIANT == 0 || RESTRAINT_VARIANT == 4
const float ReflectanceScale = 1.0;
#else
const float ReflectanceScale = 64.0;
#endif

// The weight that the colored part of a color `c` keeps: 1 in Design 2, and mix(share, 1, ramp(saturation)) in
// variant 3.
float coloredWeight(vec3 c)
{
#if RESTRAINT_VARIANT == 3
    float peak = max(c.r, max(c.g, c.b));
    float valley = min(c.r, min(c.g, c.b));
    float saturation = (peak - valley) / max(peak, 1e-12);
#if RESTRAINT_RAMP == 0
    float ramp = smoothstep(0.58, 0.78, saturation);
#elif RESTRAINT_RAMP == 1
    float ramp = min(1.0, pow(saturation / 0.78, 3.0));
#elif RESTRAINT_RAMP == 2
    float ramp = smoothstep(0.0, 0.78, saturation);
#else
#error RESTRAINT_RAMP names no ramp
#endif
    return mix(NeutralSourceShare, 1.0, ramp);
#else
    return 1.0;
#endif
}

// The reflectance words of one surface of diffuse reflectance `diffuse`, per unit of area.
vec4 capturedReflectanceOf(vec3 diffuse)
{
#if RESTRAINT_VARIANT == 2 || RESTRAINT_VARIANT == 3
    float gray = min(diffuse.r, min(diffuse.g, diffuse.b));
    return vec4(coloredWeight(diffuse) * (diffuse - gray), gray);
#else
    return vec4(diffuse, 0.0);
#endif
}

// The words that one surface of diffuse reflectance `diffuse` adds to its cell face's color, per unit of area.
vec4 capturedReflectance(vec3 diffuse)
{
    return ReflectanceScale * capturedReflectanceOf(diffuse);
}

// The light that the volume keeps of the radiance `light` that falls on gray paint: its colored part times its weight
// and `gain`, plus its gray level at the neutral share.
vec3 grayPaintKeeps(vec3 light, float gain)
{
    float gray = min(light.r, min(light.g, light.b));
    return gain * coloredWeight(light) * (light - gray) + NeutralSourceShare * gray;
}

// What gray paint keeps of one lamp's light in the first bounce, summed over the lamps by shapeLight when
// volume_shade.frag defines VOLUME_JUDGES_LAMPS. Only variant 3 with RESTRAINT_LAMPS 1 reads the sum.
vec3 lampGrayKeeps(vec3 light)
{
#if RESTRAINT_VARIANT == 3 && RESTRAINT_LAMPS == 1
    return grayPaintKeeps(light, COLOR_GAIN);
#else
    return vec3(0);
#endif
}

// The emitted radiance that one surface sends into the volume.
vec3 capturedEmission(vec3 glow)
{
#if RESTRAINT_VARIANT == 2 || RESTRAINT_VARIANT == 3 || RESTRAINT_VARIANT == 4
    return glow;
#else
    return restrainNeutral(glow);
#endif
}

// Area times the diffuse radiance that a cell face reflects: `color` is the face's summed color words, `area` its
// summed area, `light` the radiance that falls on it, `grayKeeps` what gray paint keeps of that light
// (grayPaintKeeps, or the sum over the lamps), and `gain` the color gain of the bounce (1 for the second bounce).
vec3 reflectedLight(vec4 color, float area, vec3 light, vec3 grayKeeps, float gain)
{
#if RESTRAINT_VARIANT == 2 || RESTRAINT_VARIANT == 3
    return (gain * color.rgb * light + color.w * grayKeeps) / ReflectanceScale;
#else
    return area * restrainNeutral(color.rgb * light / area);
#endif
}
