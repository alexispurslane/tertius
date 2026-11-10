// The light pyramid (ARCHITECTURE.md, "Guided diffuse rays"): its finest level holds each pixel's guide light, the
// luminance of the light that leaves the pixel without its history (light_pyramid_finest.comp), and each cell of level k
// the sum of the up to four cells under it, in the order (0, 0), (1, 0), (0, 1), (1, 1). Mip m of the levels image holds
// level m + 1. LightPyramid is the C# copy; change both together.
#ifndef TERTIUS_LIGHT_PYRAMID_GLSL
#define TERTIUS_LIGHT_PYRAMID_GLSL

// The light of a cell that lies beyond the screen.
const float NoLight = 0.0;

// The sum of two cells' light.
float summedLight(float a, float b)
{
    return a + b;
}

#endif
