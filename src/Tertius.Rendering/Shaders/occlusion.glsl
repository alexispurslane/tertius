// The occlusion passes' set and push constants (docs/plans/ambient-occlusion.md, "Push constants and sets").
// OcclusionPushConstants in GpuTypes.cs mirrors the push block. Change both together.
//
// Engine glue around the port of XeGTAO in gtao.glsl: the images, the camera and the reads of the G-buffer. View space
// is XeGTAO's: x right, y up, z forward, away from the eye. The image rows run down the screen.
#ifndef TERTIUS_OCCLUSION_GLSL
#define TERTIUS_OCCLUSION_GLSL

#extension GL_EXT_samplerless_texture_functions : require

#include "octahedral.glsl"

layout(set = 0, binding = 0) uniform texture2D gbufferDistance;     // R32F full: distance to the eye, 0 for sky
layout(set = 0, binding = 1) uniform texture2D gbufferNormal;       // RGBA16 SNORM full: octahedral, rg shading, ba geometric
layout(set = 0, binding = 2, r32f) uniform image2D viewDepth;       // half: view depth in m, SkyDepth for sky
layout(set = 0, binding = 3, r8) uniform image2D occlusionEdges;    // half: XeGTAO's packed edges
layout(set = 0, binding = 4, r16f) uniform image2D occlusionTermA;  // half
layout(set = 0, binding = 5, r8) uniform image2D occlusionTarget;   // full
layout(set = 1, binding = 0, r16f) uniform image2D denoiseSource;
layout(set = 1, binding = 1, r16f) uniform image2D denoiseDestination;

layout(push_constant, std430) uniform OcclusionPush
{
    vec4 cameraRight;     // xyz the unit right in world axes, w t_x
    vec4 cameraUp;        // xyz the unit up, w t_y
    vec4 cameraForward;   // xyz the unit forward
    float radiusMeters;
    uint slices;
    uint stepsPerSide;
    uint lastDenoise;
} occlusion;

// The full resolution, W × H pixels.
ivec2 fullSize()
{
    return textureSize(gbufferDistance, 0);
}

// The half resolution, ⌈W / 2⌉ × ⌈H / 2⌉ texels.
ivec2 halfSize()
{
    return imageSize(viewDepth);
}

bool insideHalf(ivec2 texel)
{
    return all(greaterThanEqual(texel, ivec2(0))) && all(lessThan(texel, halfSize()));
}

// The screen position of the center of a full-resolution pixel, from (0, 0) at the top left to (1, 1).
vec2 pixelScreenPosition(ivec2 pixel)
{
    return (vec2(pixel) + 0.5) / vec2(fullSize());
}

// p(z) = 4 t_x z / W: the width in m of one half-resolution texel at the view depth z (HorizonSearch.TexelWidth).
float texelWidth(float z)
{
    return 4.0 * occlusion.cameraRight.w * z / float(fullSize().x);
}

// A world-space direction in view space: its dot products with the camera's unit axes.
vec3 toViewAxes(vec3 world)
{
    return vec3(dot(world, occlusion.cameraRight.xyz), dot(world, occlusion.cameraUp.xyz),
                dot(world, occlusion.cameraForward.xyz));
}

// The view-space shading normal of a full-resolution pixel.
vec3 shadingNormalAt(ivec2 pixel)
{
    return toViewAxes(octDecode(texelFetch(gbufferNormal, pixel, 0).rg));
}

// The view-space geometric normal of a full-resolution pixel.
vec3 geometricNormalAt(ivec2 pixel)
{
    return toViewAxes(octDecode(texelFetch(gbufferNormal, pixel, 0).ba));
}

// The view depth of a half-resolution texel. A texel outside the image reads the border texel, as XeGTAO's clamped
// sampler does.
float viewDepthAt(ivec2 texel)
{
    return imageLoad(viewDepth, clamp(texel, ivec2(0), halfSize() - 1)).r;
}

#endif
