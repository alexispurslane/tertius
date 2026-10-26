#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "material.glsl"
#include "lighting.glsl"

// One surface of an environment face, drawn with mesh.vert in one of the face's three depth ranges
// (EnvironmentFacePass.cs). It is impostor_capture.frag without the ambient light (docs/plans/environment-map.md, "How a
// face is lit"): every light, a cascade set by the finest cascade whose box holds the point, and the emission, in plain
// radiance. The map is the light that arrives at the camera's eye, so V points toward the eye at the origin.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uvec2 inMaterialAndMap;   // x: the material. A face reads no instance map (see mesh.vert)

layout(location = 0) out vec4 outColor;   // RGBA16F: plain radiance, and an open-sky share of 0

void main()
{
    // A face's eye is the camera's, at the origin (draw.eye is 0). A surface there has no direction toward it, and one
    // whose vertex normal has no direction faces the eye.
    vec3 V = towardEye(inPosition);
    SurfaceSample s = sampleMaterial(inMaterialAndMap.x, inTexCoord, inNormal, inTangent, V);
    ShadingInput shading;
    shading.baseColor = s.baseColor.rgb;
    shading.normal = s.normal;
    shading.geometricNormal = normalizedOr(inNormal, V);
    shading.metallic = s.metallic;
    shading.roughness = s.roughness;
    shading.reflectance = s.reflectance;
    // No ambient light reads them.
    shading.skyFraction = 0.0;
    shading.lobeSkyFraction = 0.0;
    shading.reflectionFootprint = frame.params.z;
    shading.map = draw.map;
    // A face of map i keeps the lights of maps 0 to i: a map leaves out the lamps of the subtree that it leaves out
    // (docs/plans/environment-maps-per-frame.md, decision 13).
    outColor = vec4(directRadiance(shading, s.emissive, inPosition, V, ShadowMapsHolding, draw.map), 0.0);
}
