// The occlusion passes' set and push constants (docs/plans/screen-space-illumination.md, "Push constants and sets").
// OcclusionPushConstants in GpuTypes.cs mirrors the push block. Change both together.
//
// Engine glue around the port of XeGTAO in gtao.glsl: the images, the camera and the reads of the G-buffer. View space
// is XeGTAO's: x right, y up, z forward, away from the eye. The image rows run down the screen. The passes bind the
// scene's sets 0 to 2, so the camera is the frame constants' and the distance and the normals are the G-buffer set's.
#ifndef TERTIUS_OCCLUSION_GLSL
#define TERTIUS_OCCLUSION_GLSL

#include "frame.glsl"
#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"

layout(set = 3, binding = 0, r32f) uniform image2D viewDepth;          // half: view depth in m, SkyDepth for sky
layout(set = 3, binding = 1, r8) uniform image2D occlusionEdges;       // half: XeGTAO's packed edges
layout(set = 3, binding = 2, rgba16f) uniform image2D occlusionTermA;   // half: rgb the bounce G, pre-exposed, a V
layout(set = 3, binding = 3, rgba16f) uniform image2D estimateTarget;   // full: the same
// Slices words per half-resolution texel, row by row: each slice's mask of the sectors that its near steps block.
layout(set = 3, binding = 4, std430) buffer SectorMasks
{
    uint sectorMasks[];
};
layout(set = 3, binding = 5) uniform texture2D depthPyramid;           // SSR's pyramid, General: mip m = level m + 1
layout(set = 3, binding = 6, rgba16f) uniform image2D denoiseSource;
layout(set = 3, binding = 7, rgba16f) uniform image2D denoiseDestination;

layout(push_constant, std430) uniform OcclusionPush
{
    float radiusMeters;
    uint slices;
    uint stepsPerSide;
    uint farSteps;
    uint lastDenoise;
} occlusion;

// (t_x, t_y): the tangents of half the horizontal and half the vertical field of view. The frame constants carry them
// as the lengths of the camera's right and up.
vec2 cameraTangents()
{
    return vec2(length(frame.cameraRight.xyz), length(frame.cameraUp.xyz));
}

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

// The index of a slice's mask in the sector masks buffer. The texel lies inside the half image, so the index lies inside
// the buffer, which holds Slices words per texel.
uint maskIndex(ivec2 texel, int slice)
{
    return (uint(texel.y) * uint(halfSize().x) + uint(texel.x)) * occlusion.slices + uint(slice);
}

// The screen position of the center of a full-resolution pixel, from (0, 0) at the top left to (1, 1).
vec2 pixelScreenPosition(ivec2 pixel)
{
    return (vec2(pixel) + 0.5) / vec2(fullSize());
}

// p(z) = 4 t_x z / W: the width in m of one half-resolution texel at the view depth z (HorizonSearch.TexelWidth).
float texelWidth(float z)
{
    return 4.0 * cameraTangents().x * z / float(fullSize().x);
}

// A world-space direction in view space: its dot products with the camera's unit axes.
vec3 toViewAxes(vec3 world)
{
    vec2 tangents = cameraTangents();
    return vec3(dot(world, frame.cameraRight.xyz / tangents.x), dot(world, frame.cameraUp.xyz / tangents.y),
                dot(world, frame.cameraForward.xyz));
}

// A view-space vector in the camera-relative world axes of SSR's trace: the inverse of toViewAxes.
vec3 toWorldAxes(vec3 view)
{
    vec2 tangents = cameraTangents();
    return view.x * frame.cameraRight.xyz / tangents.x + view.y * frame.cameraUp.xyz / tangents.y
           + view.z * frame.cameraForward.xyz;
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
