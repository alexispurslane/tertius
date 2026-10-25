// The bloom pass (ARCHITECTURE.md, "Bloom"): every draw reads one image, the HDR image or a bloom level, through the
// linear clamp sampler (linear, clamped to the edge), and writes the next level.

layout(set = 0, binding = 0) uniform texture2D source;
layout(set = 0, binding = 1) uniform sampler linearClamp;

layout(push_constant, std430) uniform BloomPush
{
    float threshold;   // the brightness after the exposure, as the largest channel, at the center of the soft knee
    float knee;        // the half-width of the soft knee around the threshold, as a fraction of it
    float exposure;    // the factor from the HDR image's values to the light after the exposure
    uint levels;       // how many levels the up chain sums into level 0
} bloom;

layout(location = 0) out vec4 outColor;

// Jimenez's 13-tap downsample (Call of Duty: Advanced Warfare, SIGGRAPH 2014), from the 13 bilinear taps a to m at these
// offsets, in source texels from the output pixel's center, which lies on a source texel corner:
//   a(-2,-2) b(0,-2) c(2,-2)
//        d(-1,-1) e(1,-1)
//   f(-2, 0) g(0, 0) h(2, 0)
//        i(-1, 1) j(1, 1)
//   k(-2, 2) l(0, 2) m(2, 2)
// Five boxes of four taps each: the inner box d e i j weighs 0.5, and the four corner boxes weigh 0.125 each. No Karis
// weighting, so a bright source of a few pixels keeps its full energy (ticket ter-63wr, user decision).
vec3 jimenez13(vec3 a, vec3 b, vec3 c, vec3 d, vec3 e, vec3 f, vec3 g, vec3 h, vec3 i, vec3 j, vec3 k, vec3 l, vec3 m)
{
    vec3 inner = (d + e + i + j) * 0.25;
    vec3 corners = (a + b + f + g) * 0.25 + (b + c + g + h) * 0.25 + (f + g + k + l) * 0.25 + (g + h + l + m) * 0.25;
    return inner * 0.5 + corners * 0.125;
}
