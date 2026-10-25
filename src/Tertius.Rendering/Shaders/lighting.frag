#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"
#include "lighting.glsl"
#include "reflection_footprint.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outColor;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    vec3 direction = normalize(inRay);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    // The footprint takes the derivatives of d, which are defined only in uniform control flow, so every pixel reads its
    // normal and roughness before the test for sky pixels. A sky pixel's d is its view ray, which keeps the derivatives
    // of its lit neighbors finite.
    ShadingInput s;
    vec4 normals = texelFetch(gbufferNormal, pixel, 0);
    s.normal = octDecode(normals.rg);
    vec4 specular = texelFetch(gbufferSpecular, pixel, 0);
    s.roughness = specular.g;
    vec3 dominant = distanceToEye > 0.0 ? ambientSpecularDirection(s, -direction) : direction;
    s.reflectionFootprint = reflectionFootprint(dominant);
    if (distanceToEye <= 0.0)
    {
        outColor = vec4(preExposed(skyRadiance(direction) + starRadiance(direction)), 1.0);
        return;
    }

    // The G-buffer pass pre-exposed the emission.
    vec4 emissiveSky = texelFetch(gbufferEmissive, pixel, 0);
    vec3 emissive = emissiveSky.rgb;
    vec4 baseColor = texelFetch(gbufferBaseColor, pixel, 0);
    // Base color alpha 0 marks an unlit pixel: an impostor, whose lighting is baked into its emission.
    if (baseColor.a == 0.0)
    {
        outColor = vec4(emissive, 1.0);
        return;
    }

    // The position comes from the ray and the stored distance, so it does not depend on the projection of the
    // depth range that drew the pixel.
    vec3 position = direction * distanceToEye;
    s.baseColor = baseColor.rgb;
    s.geometricNormal = octDecode(normals.ba);
    s.metallic = specular.r;
    s.reflectance = specular.b;
    s.lobeSkyFraction = specular.a;
    s.skyFraction = emissiveSky.a;
    s.occlusion = texelFetch(gbufferOcclusion, pixel, 0).r;

    outColor = vec4(surfaceRadiance(s, emissive, position, -direction, SunShadowsMainView, exposureScale()), 1.0);
}
