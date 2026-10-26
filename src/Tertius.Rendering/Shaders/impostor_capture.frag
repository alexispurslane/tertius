#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "material.glsl"
#include "lighting.glsl"

// One object alone, rendered with mesh.vert into its impostor atlas tile (ImpostorPass.cs). draw.viewProj is a
// perspective projection from the main view's eye, fitted to the bounding sphere's tangent cone, with clip w equal
// to the view depth over the center's distance; draw.eye is that eye, so view-dependent shading matches what the
// camera sees. Lighting is the one-pass
// surfaceRadiance with the hemisphere ambient and its specular part, the sun with the analytic eclipse term, and the
// point and spot lights, but not the shadow maps: they cover the main view, not the capture. ImpostorManager recaptures when any of that
// light changes at the object.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uint inMaterial;
layout(location = 5) flat in vec4 inHemisphere;

layout(location = 0) out vec4 outColor;   // RGBA16F: lit color premultiplied by coverage (1 here), coverage
layout(location = 1) out float outDepth;  // R16F: view depth over the center's depth, minus 1, times coverage

void main()
{
    vec3 V = towardEye(inPosition);
    SurfaceSample s = sampleMaterial(inMaterial, inTexCoord, inNormal, inTangent, V);
    ShadingInput shading;
    shading.baseColor = s.baseColor.rgb;
    shading.normal = s.normal;
    shading.geometricNormal = normalizedOr(inNormal, V);
    shading.metallic = s.metallic;
    shading.roughness = s.roughness;
    shading.reflectance = s.reflectance;
    shading.skyFraction = skyFraction(inHemisphere, inPosition, s.normal);
    shading.lobeSkyFraction = lobeSkyFraction(inHemisphere, inPosition, s.normal, V, s.roughness);
    // A capture has no screen around it for the occlusion passes to search, so no estimate.
    shading.occlusion = 1.0;
    shading.bounce = vec3(0.0);
    // A scale of 1: the atlas stores radiance, and the billboard pre-exposes it in the frame that draws it.
    vec3 color = surfaceRadiance(shading, s.emissive, inPosition, V, false, 1.0);
    outColor = vec4(color, 1.0);
    // gl_FragCoord.w is 1 / clip w, and clip w is the view depth over the center's depth. Negative in front of the
    // center's plane.
    outDepth = 1.0 / gl_FragCoord.w - 1.0;
}
