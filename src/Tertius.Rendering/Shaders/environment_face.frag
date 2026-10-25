#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "material.glsl"
#include "lighting.glsl"

// One surface of an environment face, drawn with mesh.vert in one of the face's three depth ranges
// (EnvironmentFacePass.cs). It is impostor_capture.frag without the ambient light (docs/plans/environment-map.md, "How a
// face is lit"): the sun, by the finest cascade whose box holds the point, the point and spot lights, and the emission,
// in plain radiance. The map is the light that arrives at the camera's eye, so V points toward the eye at the origin.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uint inMaterial;

layout(location = 0) out vec4 outColor;   // RGBA16F: plain radiance, and an open-sky share of 0

void main()
{
    SurfaceSample s = sampleMaterial(inMaterial, inTexCoord, inNormal, inTangent);
    ShadingInput shading;
    shading.baseColor = s.baseColor.rgb;
    shading.normal = s.normal;
    shading.geometricNormal = normalize(inNormal);
    shading.metallic = s.metallic;
    shading.roughness = s.roughness;
    shading.reflectance = s.reflectance;
    // No ambient light reads them.
    shading.skyFraction = 0.0;
    shading.lobeSkyFraction = 0.0;
    shading.reflectionFootprint = frame.params.z;
    vec3 V = normalize(-inPosition);
    outColor = vec4(directRadiance(shading, s.emissive, inPosition, V, SunShadowsHolding), 0.0);
}
