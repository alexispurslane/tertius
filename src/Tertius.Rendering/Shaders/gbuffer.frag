#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "material.glsl"
#include "octahedral.glsl"
#include "hemisphere.glsl"
#include "exposure.glsl"
#include "environment.glsl"
#include "motion.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uvec4 inInstanceWords;  // x: the material. y: the instance's map. z: GpuInstance.holdsLight. w:
                                                     // the card code (mesh.vert)
layout(location = 5) flat in vec4 inHemisphere;
layout(location = 6) in vec3 inMotionNow;   // this frame's and the last frame's motion clip x, y and w (mesh.vert)
layout(location = 7) in vec3 inMotionLast;

layout(location = 0) out vec4 outBaseColor;
layout(location = 1) out vec4 outNormal;
layout(location = 2) out uvec4 outSpecular;  // the 8-bit codes of RenderTargets.SpecularFormat
layout(location = 3) out vec4 outEmissive;
layout(location = 4) out float outDistance;
layout(location = 5) out uvec4 outShading;
layout(location = 6) out vec4 outMotion;

// The map code (docs/plans/environment-maps-per-frame.md, decision 20): map i owns the codes 255 − 2i and 256 − 2i of the
// 8-bit alpha, and the value lies between them, so either rounding that the conversion to UNORM may take names map i.
// Map 0 writes 1.0, which converts to 255 exactly. MapCode.Of is the C# twin, and mapOfCode in gbuffer_inputs.glsl reads it.
float mapCode(uint map)
{
    return min((255.5 - 2.0 * float(map)) / 255.0, 1.0);
}

// The 8-bit codes of values from 0 to 1, each the code nearest to the value, the upper one halfway. The multiply and the
// add are correctly rounded in Vulkan and floor is exact, so every driver gives one code. A UNORM store may take either
// of the two codes nearest to the value, and NVIDIA's store and Apple's took different codes for a roughness of 0.1
// (ticket ter-8esk).
uvec4 unorm8Codes(vec4 values)
{
    return uvec4(floor(clamp(values, 0.0, 1.0) * 255.0 + 0.5));
}

void main()
{
    // A surface at the eye has no direction toward it, and one whose vertex normal has no direction faces the eye.
    vec3 V = towardEye(inPosition);
    SurfaceSample s = sampleMaterial(inInstanceWords.x, inTexCoord, inNormal, inTangent, V);
    outBaseColor = vec4(s.baseColor.rgb, mapCode(inInstanceWords.y));
    // The shading normal for lighting, a hair's fiber normal, and the vertex normal for shadow-map bias: a normal map does
    // not move the surface that the shadow map sees.
    outNormal = vec4(octEncode(s.normal), octEncode(s.geometricNormal));
    // The alphas: how much of the hemisphere ambient light the lobe and the normal get from the sky. Both need the
    // instance's up, which only this pass knows. Only the hemisphere reads them, so with the map on they hold 0.
    float lobeSky = environmentMapOn ? 0.0 : lobeSkyFraction(inHemisphere, inPosition, s.normal, V, s.roughness);
    float normalSky = environmentMapOn ? 0.0 : skyFraction(inHemisphere, inPosition, s.normal);
    outSpecular = unorm8Codes(vec4(s.metallic, s.roughness, s.reflectance, lobeSky));
    outEmissive = vec4(preExposed(s.emissive), normalSky);
    // Euclidean distance, not depth: the direct and ambient passes rebuild the position as ray × distance, which does not
    // depend on which depth range drew the pixel.
    outDistance = length(inPosition - draw.eye.xyz);
    // The model's code, a hair's strand and card code (docs/plans/skin-and-hair.md, "The shading target"), and whether
    // the entity holds a light, whose face its glow is (docs/plans/screen-space-illumination.md, "The bounce source").
    outShading = uvec4(s.model == ShadingHair ? strandCode(s.strand) : uvec2(0u),
                       lightModelAndCardWords(inInstanceWords.z, s.model, inInstanceWords.w));
    outMotion = motionOf(inMotionNow, inMotionLast);
}
