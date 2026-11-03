#version 460
// The probe of the direct light (DirectLightProbe.cs): the composite without the ambient light and the
// sky, which gives the direct light and the emission of each pixel, as the direct image held them before the direct signal.
#extension GL_GOOGLE_include_directive : require
#define TERTIUS_DIRECT_LIGHT_ONLY
#include "composite_pass.glsl"
