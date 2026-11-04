#version 460
// The probe of the direct diffuse light (DirectLightProbe.cs): the diffuse factor times the direct
// signal's diffuse history of each lit pixel, as the diffuse image held it before the direct signal.
#extension GL_GOOGLE_include_directive : require
#define TERTIUS_DIRECT_LIGHT_ONLY
#define TERTIUS_DIRECT_DIFFUSE_ONLY
#include "composite_pass.glsl"
