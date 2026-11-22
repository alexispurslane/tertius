#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "material.glsl"
#include "back_face.glsl"

// The back-face pass's cutout runs: the G-buffer's cutout test, then the fragment rule of back_face.glsl. The pipeline
// sets ShaderConstants.MainView, so the sample takes the main view's mip bias and keeps the texels that the G-buffer
// keeps. The sample comes before every discard, so that its implicit derivatives stay defined.
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inTexCoord;
layout(location = 2) flat in uint inMaterial;

void main()
{
    GpuMaterial m = materials[inMaterial];
    float alpha = m.baseColor.a * sampleTexture(m.baseColorMap, inTexCoord).a;
    if (!reachesCutoff(alpha, m.alphaCutoff))
        discard;
    writeBackShare(inPosition);
}
