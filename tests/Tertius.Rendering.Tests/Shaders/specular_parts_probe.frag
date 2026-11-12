#version 460
// THROWAWAY DIAGNOSTIC (ter-8esk): the raw direct specular, the specular factor, the indirect specular and the direct diffuse.
#extension GL_GOOGLE_include_directive : require
#define TERTIUS_SPECULAR_PARTS_PROBE
#include "composite_pass.glsl"
