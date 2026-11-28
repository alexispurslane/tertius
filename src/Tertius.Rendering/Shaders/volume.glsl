// The directional light volume's buffer (volume_layout.glsl has the grids' shape).
// Each signed dominant-normal bucket sums geometry in units of 1/4096 of one cell face.
#include "volume_layout.glsl"
const float EmissionScale = 4.0;
const float NeutralSourceShare = 0.02;
// The color gain of the restraint: a saturated color's kept light times ColorGain, through the ramp, so a faint tint
// keeps nearly its plain weight (restraintWeight, gain). 1 in the shipped rule. The paint's part acts in the capture,
// so its sums carry the gain into the second bounce too. A value other than 1 needs a plain paint sum for the second
// bounce first.
const float ColorGain = 1.0;
// The color words of a cell face sum products as small as area x 0.02 x reflectance in integers, and one fragment adds
// up to about 256 area units. One step per unit biased a gray fragment by up to 15%. 64 steps per unit keep about 200
// steps for one fragment of 0.65 gray paint at the neutral share.
const float ReflectanceScale = 64.0;
struct SurfaceBucket
{
    ivec4 areaNormal; // xyz oriented area vector, w area
    // The paint sums of the restraint (capturedPaint), in ReflectanceScale steps per unit of area x reflectance: rgb
    // area times the paints' weighted colored parts, w area times their gray levels.
    uvec4 color;
    // rgb holds one of two things, by pass. After the capture (volume_surface.frag): area times emission, as integers
    // at EmissionScale. After the shading (volume_shade.frag): area times total outgoing radiance, as float bits, in
    // every bucket with area. A raster write adds at least the 256 units of one sample's projected area, so a bucket
    // with nonzero emission words has area, and the restriction and the source pass read only float bits. w is unused.
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

// THE RESTRAINT (issue #42): how much of each surface's bounce light the volume keeps. Gray light on gray paint keeps
// NeutralSourceShare, so a gray room's bounce does not flatten its contrast, and saturated light keeps its full
// strength. The rule judges one surface and one light. It splits each color into its gray level, the smallest
// channel, and its colored part, the color minus the gray level. The colored part keeps the weight of the color's
// saturation (restraintWeight), and the gray level keeps NeutralSourceShare. Nothing moves from a colored part into a
// gray level, so a faint tint gives a dim wash of its own hue.
//
// The paint's half runs in the capture, per fragment (capturedPaint): each cell face sums area times the paint's
// weighted colored part in color.rgb and area times its gray level in color.w. The light's half runs where each light
// exists (grayPaintKeeps): per lamp in the first bounce (shapeLight sums it into volumeGrayPaintKeeps), and on the
// incoming bounce light in the second bounce. After that every step only adds and multiplies (reflectedLight), so one
// surface's light does not depend on the grid cell that holds it or on its neighbours in the cell. Glow enters in full.
// Three functions hold the rule: colorSplit, restraintWeight and grayPaintKeeps.

// The colored part of `c` in rgb and its gray level in w.
vec4 colorSplit(vec3 c)
{
    float gray = min(c.r, min(c.g, c.b));
    return vec4(c - gray, gray);
}

// The part of its colored part that a color keeps, from its saturation (max - min) / max: the ramp
// min(1, (saturation / 0.78)^3) takes the weight from NeutralSourceShare for a gray color to 1 at saturation 0.78 and
// above, and the gain from 1 to `gain` alike: mix(share, 1, ramp) x mix(1, gain, ramp). `split` is the color as
// colorSplit splits it.
float restraintWeight(vec4 split, float gain)
{
    float colored = max(split.r, max(split.g, split.b));
    float peak = colored + split.w;
    float ramp = min(1.0, pow((peak > 0.0 ? colored / peak : 0.0) / 0.78, 3.0));
    return mix(NeutralSourceShare, 1.0, ramp) * mix(1.0, gain, ramp);
}

// The light that gray paint of reflectance 1 keeps of the radiance `light` that falls on it: the light's colored part
// at its weight, plus its gray level at NeutralSourceShare. The first bounce passes ColorGain, the second 1.
vec3 grayPaintKeeps(vec3 light, float gain)
{
    vec4 split = colorSplit(light);
    return restraintWeight(split, gain) * split.rgb + NeutralSourceShare * split.w;
}

// The color words that a surface of diffuse reflectance `paint` adds per unit of area: its colored part at its weight
// in rgb and its gray level in w, at ReflectanceScale steps per unit.
vec4 capturedPaint(vec3 paint)
{
    vec4 split = colorSplit(paint);
    return ReflectanceScale * vec4(restraintWeight(split, ColorGain) * split.rgb, split.w);
}

// Area times the diffuse radiance that a cell face reflects. `color` is the face's color words, `light` the radiance
// that falls on it and `grayKept` what gray paint keeps of that light: grayPaintKeeps of one light, or its sum over
// the lamps. The colored paint takes the whole light, and the gray paint what it keeps.
vec3 reflectedLight(uvec4 color, vec3 light, vec3 grayKept)
{
    vec4 sums = vec4(color);
    return (sums.rgb * light + sums.w * grayKept) / ReflectanceScale;
}
