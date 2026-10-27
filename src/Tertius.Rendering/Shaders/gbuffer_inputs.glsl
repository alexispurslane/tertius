// The G-buffer as the direct, ambient and transparent passes read it (set 2), the cascade sets of the far lights, the local
// shadow atlas of the near lights, the estimate target and the direct image (docs/plans/screen-space-illumination.md,
// "Push constants and sets"), and the shading target (docs/plans/skin-and-hair.md, "The shading target"). All G-buffer
// targets have the output's size and are read with texelFetch at gl_FragCoord, so they need no sampler.
#ifndef TERTIUS_GBUFFER_INPUTS_GLSL
#define TERTIUS_GBUFFER_INPUTS_GLSL

#extension GL_EXT_samplerless_texture_functions : require

layout(set = 2, binding = 0) uniform texture2D gbufferBaseColor;       // RGBA8 sRGB: linear base color on read; alpha
                                                                       // the map code of a lit pixel (mapCode,
                                                                       // gbuffer.frag), 0 unlit (impostors: emission
                                                                       // only)
layout(set = 2, binding = 1) uniform texture2D gbufferNormal;          // RGBA16 SNORM: octahedral world normals,
                                                                       // rg shading (normal-mapped), ba geometric
layout(set = 2, binding = 2) uniform texture2D gbufferSpecular;        // RGBA8: metallic, roughness r₀, reflectance,
                                                                       // the lobe's sky share
layout(set = 2, binding = 3) uniform texture2D gbufferEmissive;        // RGBA16F: emitted radiance, the normal's sky
                                                                       // share
layout(set = 2, binding = 4) uniform texture2D gbufferDistance;        // R32F: distance to the eye, 0 for sky
layout(set = 2, binding = 5) uniform texture2DArray shadowCascades;   // D32: one layer per cascade of each set, reversed depth
layout(set = 2, binding = 6) uniform samplerShadow shadowSampler;      // linear GREATER_OR_EQUAL comparison (PCF)
layout(set = 2, binding = 7) uniform texture2D gbufferEstimate;        // RGBA16F: rgb the bounce G, pre-exposed, a the
                                                                       // occlusion V
layout(set = 2, binding = 8) uniform texture2DArray shadowAtlas;      // D32: one tile per layer, reversed depth;
                                                                       // partially bound, empty without the atlas
layout(set = 2, binding = 9) uniform texture2D gbufferDirect;          // RGBA16F: the direct light, pre-exposed, 0 for
                                                                       // sky
layout(set = 2, binding = 10) uniform utexture2D gbufferShading;       // RGBA8 UINT: rg a hair pixel's strand code (strandCode,
                                                                       // octahedral.glsl), b 1 where the entity holds a
                                                                       // light, a the shading model's code;
                                                                       // 0 where nothing lit draws

#endif
