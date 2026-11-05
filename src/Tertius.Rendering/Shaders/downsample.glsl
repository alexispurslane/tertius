// Jimenez's 13-tap downsample (Call of Duty: Advanced Warfare, SIGGRAPH 2014), which the bloom's down chain takes, from
// the 13 bilinear taps a to m at these offsets, in source texels from
// the output pixel's center:
//   a(-2,-2) b(0,-2) c(2,-2)
//        d(-1,-1) e(1,-1)
//   f(-2, 0) g(0, 0) h(2, 0)
//        i(-1, 1) j(1, 1)
//   k(-2, 2) l(0, 2) m(2, 2)
// Five boxes of four taps each: the inner box d e i j weighs 0.5, and the four corner boxes weigh 0.125 each. No Karis
// weighting, so a bright source of a few pixels keeps its full energy (ticket ter-63wr, user decision).
#ifndef TERTIUS_DOWNSAMPLE_GLSL
#define TERTIUS_DOWNSAMPLE_GLSL

vec4 jimenez13(vec4 a, vec4 b, vec4 c, vec4 d, vec4 e, vec4 f, vec4 g, vec4 h, vec4 i, vec4 j, vec4 k, vec4 l, vec4 m)
{
    vec4 inner = (d + e + i + j) * 0.25;
    vec4 corners = (a + b + f + g) * 0.25 + (b + c + g + h) * 0.25 + (f + g + k + l) * 0.25 + (g + h + l + m) * 0.25;
    return inner * 0.5 + corners * 0.125;
}

#endif
