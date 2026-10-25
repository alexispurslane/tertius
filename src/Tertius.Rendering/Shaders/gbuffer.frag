#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "material.glsl"
#include "octahedral.glsl"
#include "hemisphere.glsl"
#include "exposure.glsl"
#include "environment.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uint inMaterial;
layout(location = 5) flat in vec4 inHemisphere;

layout(location = 0) out vec4 outBaseColor;
layout(location = 1) out vec4 outNormal;
layout(location = 2) out vec4 outSpecular;
layout(location = 3) out vec4 outEmissive;
layout(location = 4) out float outDistance;

void main()
{
    SurfaceSample s = sampleMaterial(inMaterial, inTexCoord, inNormal, inTangent);
    outBaseColor = vec4(s.baseColor.rgb, 1.0);
    // The shading normal for lighting, and the vertex normal for shadow-map bias: a normal map does not move the
    // surface that the shadow map sees.
    outNormal = vec4(octEncode(s.normal), octEncode(normalize(inNormal)));
    // The alphas: how much of the hemisphere ambient light the lobe and the normal get from the sky. Both need the
    // instance's up, which only this pass knows. Only the hemisphere reads them, so with the map on they hold 0.
    vec3 V = normalize(draw.eye.xyz - inPosition);
    float lobeSky = environmentMapOn ? 0.0 : lobeSkyFraction(inHemisphere, inPosition, s.normal, V, s.roughness);
    float normalSky = environmentMapOn ? 0.0 : skyFraction(inHemisphere, inPosition, s.normal);
    outSpecular = vec4(s.metallic, s.roughness, s.reflectance, lobeSky);
    outEmissive = vec4(preExposed(s.emissive), normalSky);
    // Euclidean distance, not depth: the lighting pass rebuilds the position as ray × distance, which does not
    // depend on which depth range drew the pixel.
    outDistance = length(inPosition - draw.eye.xyz);
}
