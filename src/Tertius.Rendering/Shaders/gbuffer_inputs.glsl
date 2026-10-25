// The G-buffer as the lighting and transparent passes read it (set 2), and the sun shadow cascades. All G-buffer
// targets have the output's size and are read with texelFetch at gl_FragCoord, so they need no sampler.
#ifndef TERTIUS_GBUFFER_INPUTS_GLSL
#define TERTIUS_GBUFFER_INPUTS_GLSL

#extension GL_EXT_samplerless_texture_functions : require

layout(set = 2, binding = 0) uniform texture2D gbufferBaseColor;       // RGBA8 sRGB: linear base color on read; alpha 1
                                                                       // lit, 0 unlit (impostors: emission only)
layout(set = 2, binding = 1) uniform texture2D gbufferNormal;          // RGBA16 SNORM: octahedral world normals,
                                                                       // rg shading (normal-mapped), ba geometric
layout(set = 2, binding = 2) uniform texture2D gbufferSpecular;        // RGBA8: metallic, roughness r₀, reflectance,
                                                                       // the lobe's sky share
layout(set = 2, binding = 3) uniform texture2D gbufferEmissive;        // RGBA16F: emitted radiance, the normal's sky
                                                                       // share
layout(set = 2, binding = 4) uniform texture2D gbufferDistance;        // R32F: distance to the eye, 0 for sky
layout(set = 2, binding = 5) uniform texture2DArray shadowCascades;   // D32: one layer per cascade, reversed depth
layout(set = 2, binding = 6) uniform samplerShadow shadowSampler;      // linear GREATER_OR_EQUAL comparison (PCF)
layout(set = 2, binding = 7) uniform texture2D gbufferOcclusion;       // R8: the occlusion V of each pixel, 1 when off

#endif
