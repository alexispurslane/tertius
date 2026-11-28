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
// Stage 2 keeps the chosen rule and removes the switch and the other variants.
//   0: today (eff66e481): the restraint reads each cell face's summed color.
//   1: Design 1, the paint decides: the capture weights each surface's reflectance by the knee of its saturation.
//   2: Design 2, only gray on gray is held back: the capture splits each reflectance into a colored part (color.rgb)
//      and a gray level (color.w), and the bounce keeps the neutral share of gray level times the light's gray level.
//   3: Hybrid: Design 2's split, with the knee on the paint and on the light. Each knee moves the colored part that it
//      drops into the gray level at equal luminance.
// After the capture, variants 1 to 3 only add sums and multiply them by the light.
#ifndef RESTRAINT_VARIANT
#define RESTRAINT_VARIANT 0
#endif
const vec3 RestraintLuminance = vec3(0.2126, 0.7152, 0.0722);
// Variants 1 to 3 store small products, such as area x 0.02 x reflectance, in the integer color words. One fragment
// adds up to about 256 area units, so 64 steps per unit keep about 200 steps for one fragment of 0.65 gray paint at
// the neutral share.
#if RESTRAINT_VARIANT == 0
const float ReflectanceScale = 1.0;
#else
const float ReflectanceScale = 64.0;
#endif

// The reflectance words of one surface of diffuse reflectance `diffuse`, per unit of area.
vec4 capturedReflectanceOf(vec3 diffuse)
{
#if RESTRAINT_VARIANT == 1
    return vec4(diffuse * mix(NeutralSourceShare, 1.0, saturationKnee(diffuse)), 0.0);
#elif RESTRAINT_VARIANT == 2
    float gray = min(diffuse.r, min(diffuse.g, diffuse.b));
    return vec4(diffuse - gray, gray);
#elif RESTRAINT_VARIANT == 3
    float gray = min(diffuse.r, min(diffuse.g, diffuse.b));
    vec3 colored = diffuse - gray;
    float kept = saturationKnee(diffuse);
    return vec4(kept * colored, gray + dot((1.0 - kept) * colored, RestraintLuminance));
#else
    return vec4(diffuse, 0.0);
#endif
}

// The words that one surface of diffuse reflectance `diffuse` adds to its cell face's color, per unit of area.
vec4 capturedReflectance(vec3 diffuse)
{
    return ReflectanceScale * capturedReflectanceOf(diffuse);
}

// The light that the volume keeps of the radiance `light` that falls on gray paint: the gray level of the light at
// the neutral share, and its colored part in full. Variant 3 first moves the colored part that the knee drops into the
// gray level at equal luminance.
vec3 grayPaintKeeps(vec3 light)
{
    float gray = min(light.r, min(light.g, light.b));
    vec3 colored = light - gray;
#if RESTRAINT_VARIANT == 3
    float kept = saturationKnee(light);
    gray += dot((1.0 - kept) * colored, RestraintLuminance);
    colored *= kept;
#endif
    return colored + NeutralSourceShare * gray;
}

// The emitted radiance that one surface sends into the volume.
vec3 capturedEmission(vec3 glow)
{
#if RESTRAINT_VARIANT == 2 || RESTRAINT_VARIANT == 3
    return grayPaintKeeps(glow);
#else
    return restrainNeutral(glow);
#endif
}

// Area times the diffuse radiance that a cell face reflects: `color` is the face's summed color words, `area` its
// summed area, and `light` the radiance that falls on it.
vec3 reflectedLight(vec4 color, float area, vec3 light)
{
#if RESTRAINT_VARIANT == 1
    return color.rgb / ReflectanceScale * light;
#elif RESTRAINT_VARIANT == 2 || RESTRAINT_VARIANT == 3
    return (color.rgb * light + color.w * grayPaintKeeps(light)) / ReflectanceScale;
#else
    return area * restrainNeutral(color.rgb * light / area);
#endif
}
