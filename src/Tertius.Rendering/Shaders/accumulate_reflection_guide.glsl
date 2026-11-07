#ifndef TERTIUS_ACCUMULATE_REFLECTION_GUIDE_GLSL
#define TERTIUS_ACCUMULATE_REFLECTION_GUIDE_GLSL

#include "reflection_distance_guide.glsl"

layout(set = 3, binding = 9, rgba16f) uniform image2D reflectionGuide;
layout(set = 3, binding = 10, rgba16f) uniform image2D reflectionGuideScratch;

#endif
