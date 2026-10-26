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
layout(location = 4) flat in uvec2 inMaterialAndMap;   // x: the material. y: the instance's environment map
layout(location = 5) flat in vec4 inHemisphere;

layout(location = 0) out vec4 outBaseColor;
layout(location = 1) out vec4 outNormal;
layout(location = 2) out vec4 outSpecular;
layout(location = 3) out vec4 outEmissive;
layout(location = 4) out float outDistance;

// The map code (docs/plans/environment-maps-per-frame.md, decision 20): map i owns the codes 255 − 2i and 256 − 2i of the
// 8-bit alpha, and the value lies between them, so either rounding that the conversion to UNORM may take names map i.
// Map 0 writes 1.0, which converts to 255 exactly. MapCode.Of is the C# twin, and mapOfCode in gbuffer_surface.glsl reads it.
float mapCode(uint map)
{
    return min((255.5 - 2.0 * float(map)) / 255.0, 1.0);
}

void main()
{
    // A surface at the eye has no direction toward it, and one whose vertex normal has no direction faces the eye.
    vec3 V = towardEye(inPosition);
    SurfaceSample s = sampleMaterial(inMaterialAndMap.x, inTexCoord, inNormal, inTangent, V);
    outBaseColor = vec4(s.baseColor.rgb, mapCode(inMaterialAndMap.y));
    // The shading normal for lighting, and the vertex normal for shadow-map bias: a normal map does not move the
    // surface that the shadow map sees.
    outNormal = vec4(octEncode(s.normal), octEncode(normalizedOr(inNormal, V)));
    // The alphas: how much of the hemisphere ambient light the lobe and the normal get from the sky. Both need the
    // instance's up, which only this pass knows. Only the hemisphere reads them, so with the map on they hold 0.
    float lobeSky = environmentMapOn ? 0.0 : lobeSkyFraction(inHemisphere, inPosition, s.normal, V, s.roughness);
    float normalSky = environmentMapOn ? 0.0 : skyFraction(inHemisphere, inPosition, s.normal);
    outSpecular = vec4(s.metallic, s.roughness, s.reflectance, lobeSky);
    outEmissive = vec4(preExposed(s.emissive), normalSky);
    // Euclidean distance, not depth: the lighting pass rebuilds the position as ray × distance, which does not
    // depend on which depth range drew the pixel.
    outDistance = length(inPosition - draw.eye.xyz);
}
