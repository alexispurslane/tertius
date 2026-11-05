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
// camera sees. Lighting is the one-pass surfaceRadiance with the ambient light of the frame's ambient source and every
// light that the main view keeps, with its far shadows, but not the shadow maps or the tiles: they cover the main view,
// not the capture. ImpostorManager recaptures when any of that light changes at the object.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inTexCoord;
layout(location = 4) flat in uvec4 inInstanceWords;  // x: the material. y: the instance's map. z: GpuInstance.holdsLight. w:
                                                     // the card code (mesh.vert)
layout(location = 5) flat in vec4 inHemisphere;

layout(location = 0) out vec4 outColor;   // RGBA16F: lit color premultiplied by coverage (1 here), coverage
layout(location = 1) out float outDepth;  // R16F: view depth over the center's depth, minus 1, times coverage

void main()
{
    vec3 V = towardEye(inPosition);
    SurfaceSample s = sampleMaterial(inInstanceWords.x, inTexCoord, inNormal, inTangent, V);
    // The capture bakes the light of the object's own map. It has no screen around it for the screen tracer to walk, so
    // it takes the ambient light with every direction open, as a forward surface does.
    ShadingInput shading = forwardShading(s, inPosition, inHemisphere, inInstanceWords.y, V);
    // A scale of 1: the atlas stores radiance, and the billboard pre-exposes it in the frame that draws it.
    vec3 color = surfaceRadiance(shading, s.emissive, inPosition, V, ShadowMapsNone, 1.0);
    outColor = vec4(color, 1.0);
    // gl_FragCoord.w is 1 / clip w, and clip w is the view depth over the center's depth. Negative in front of the
    // center's plane.
    outDepth = 1.0 / gl_FragCoord.w - 1.0;
}
