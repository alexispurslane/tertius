#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "material.glsl"
#include "lighting.glsl"
#include "reflection_footprint.glsl"

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uint inMaterial;
layout(location = 5) flat in vec4 inHemisphere;

layout(location = 0) out vec4 outColor;

void main()
{
    // Sample and take the footprint before the discard below, so implicit derivatives stay defined for the whole quad.
    SurfaceSample s = sampleMaterial(inMaterial, inTexCoord, inNormal, inTangent);
    float distanceToEye = length(inPosition - draw.eye.xyz);
    vec3 V = (draw.eye.xyz - inPosition) / max(distanceToEye, 1e-6);
    ShadingInput shading;
    shading.normal = s.normal;
    shading.roughness = s.roughness;
    shading.reflectionFootprint = reflectionFootprint(ambientSpecularDirection(shading, V));

    // The hardware depth buffer only holds the nearest depth range, so occlusion by opaque surfaces uses the
    // G-buffer distance, which covers all ranges.
    float opaqueDistance = texelFetch(gbufferDistance, ivec2(gl_FragCoord.xy), 0).r;
    if (opaqueDistance > 0.0 && distanceToEye > opaqueDistance)
        discard;

    shading.baseColor = s.baseColor.rgb;
    shading.geometricNormal = normalize(inNormal);
    shading.metallic = s.metallic;
    shading.reflectance = s.reflectance;
    // Only the hemisphere reads the sky shares.
    shading.skyFraction = environmentMapOn ? 0.0 : skyFraction(inHemisphere, inPosition, s.normal);
    shading.lobeSkyFraction =
        environmentMapOn ? 0.0 : lobeSkyFraction(inHemisphere, inPosition, s.normal, V, s.roughness);
    // Alpha blending of two finite values stays finite, so the clamped radiance blends safely.
    float exposure = exposureScale();
    vec3 radiance = surfaceRadiance(shading, s.emissive * exposure, inPosition, V, SunShadowsMainView, exposure);
    outColor = vec4(radiance, clamp(s.baseColor.a, 0.0, 1.0));
}
