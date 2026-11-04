#version 460
// The probe of the direct diffuse light (DirectLightProbe.cs): the diffuse factor times the direct light's diffuse image
// at each lit pixel.
#extension GL_GOOGLE_include_directive : require
#define TERTIUS_DIRECT_LIGHT_ONLY
#define TERTIUS_DIRECT_DIFFUSE_ONLY
#include "composite_pass.glsl"
