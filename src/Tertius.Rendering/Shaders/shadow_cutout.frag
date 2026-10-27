#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "material.glsl"

// The cutout casters of a shadow map: a texel of the base color whose alpha is below the material's cutoff casts no
// shadow (docs/plans/skin-and-hair.md, "The cutout test"). Depth only: the rasterizer writes the depth of what remains.
layout(location = 0) in vec2 inTexCoord;
layout(location = 1) flat in uint inMaterial;

void main()
{
    GpuMaterial m = materials[inMaterial];
    float alpha = m.baseColor.a * sampleTexture(m.baseColorMap, inTexCoord).a;
    if (!reachesCutoff(alpha, m.alphaCutoff))
        discard;
}
