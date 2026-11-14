// The G-buffer as the direct pass, the composite, the screen tracer and the transparent pass read it (set 2), the cascade
// sets of the far lights, the local shadow atlas of the near lights, and the shading target
// (docs/plans/skin-and-hair.md, "The shading target"). Binding 7 held the estimate target of the occlusion passes,
// which the screen tracer replaced (docs/plans/temporal-renderer.md, section 12). All G-buffer
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
layout(set = 2, binding = 2) uniform utexture2D gbufferSpecular;       // RGBA8 UINT: the codes of metallic, roughness
                                                                       // r₀, reflectance, the lobe's sky share
                                                                       // (specularAt)
layout(set = 2, binding = 3) uniform texture2D gbufferEmissive;        // RGBA16F: emitted radiance, the normal's sky
                                                                       // share
layout(set = 2, binding = 4) uniform texture2D gbufferDistance;        // R32F: distance to the eye, 0 for sky
layout(set = 2, binding = 5) uniform texture2DArray shadowCascades;   // D32: one layer per cascade of each set, reversed depth
layout(set = 2, binding = 6) uniform samplerShadow shadowSampler;      // linear GREATER_OR_EQUAL comparison (PCF)
layout(set = 2, binding = 8) uniform texture2DArray shadowAtlas;      // D32: one tile per layer, reversed depth;
                                                                       // partially bound, empty without the atlas
layout(set = 2, binding = 10) uniform utexture2D gbufferShading;       // RGBA8 UINT: rg a hair pixel's strand code (strandCode,
                                                                       // octahedral.glsl), bit 0 of b 1 where the entity
                                                                       // holds a light, bits 0 and 1 of a the shading
                                                                       // model's code, and a hair pixel's card code above
                                                                       // them (lightModelAndCardWords, frame.glsl);
                                                                       // 0 where nothing lit draws
layout(set = 2, binding = 11) uniform texture2DArray shadowBounds;    // RG32F: per layer of the atlas, at half its side,
                                                                       // the least and the greatest reversed depth of
                                                                       // each window (shadow_bounds.comp); partially
                                                                       // bound

// The tile records of key and fill (key_fill.glsl), which only the direct pass under key and fill reads; partially
// bound, empty without it.
#include "key_fill.glsl"
layout(set = 2, binding = 7, std430) readonly buffer LightTileBlock { uint tileWords[]; };

// The specular target at the pixel: metallic, roughness r₀, reflectance and the lobe's sky share, each its code over 255.
vec4 specularAt(ivec2 pixel)
{
    return vec4(texelFetch(gbufferSpecular, pixel, 0)) / 255.0;
}

// The map that a lit pixel's map code names (docs/plans/environment-maps-per-frame.md, decision 20): code c of the 8-bit
// alpha, from 1 to 255, names map (256 − c) / 2, rounded down. MapCode.MapOf is the C# twin. Code 0 marks an unlit pixel,
// which no caller passes here.
uint mapOfCode(float alpha)
{
    return (256u - uint(round(alpha * 255.0))) >> 1;
}

#endif
