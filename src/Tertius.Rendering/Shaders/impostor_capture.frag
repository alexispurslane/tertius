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
// camera sees. Lighting is the lighting pass's
// Blinn-Phong with the hemisphere ambient, the sun with the analytic eclipse term, and the point and spot lights,
// but not the shadow maps: they cover the main view, not the capture. ImpostorManager recaptures when any of that
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
    SurfaceSample s = sampleMaterial(inMaterial, inTexCoord, inNormal, inTangent);
    ShadingInput shading;
    shading.albedo = s.albedo.rgb;
    shading.normal = s.normal;
    shading.geometricNormal = normalize(inNormal);
    shading.specular = s.specular;
    shading.gloss = s.gloss;
    shading.skyFraction = skyFraction(inHemisphere, inPosition, s.normal);
    vec3 V = normalize(draw.eye.xyz - inPosition);
    vec3 color = ambientLight(shading) + sunLight(shading, inPosition, V, false) + localLights(shading, inPosition, V) + s.emissive;
    outColor = vec4(color, 1.0);
    // gl_FragCoord.w is 1 / clip w, and clip w is the view depth over the center's depth. Negative in front of the
    // center's plane.
    outDepth = 1.0 / gl_FragCoord.w - 1.0;
}
