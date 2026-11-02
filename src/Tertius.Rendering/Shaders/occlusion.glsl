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
#include "estimate.glsl"

layout(set = 3, binding = 0, r32f) uniform image2D viewDepth;          // half: view depth in m, SkyDepth for sky
layout(set = 3, binding = 1, r8) uniform image2D occlusionEdges;       // half: XeGTAO's packed edges
// The estimate as packEstimate packs it (estimate.glsl): term A at half resolution, the estimate target at full, and the
// denoise's source and destination, term A and term B in turn.
layout(set = 3, binding = 2, rgba32ui) uniform uimage2D occlusionTermA;
layout(set = 3, binding = 3, rgba32ui) uniform uimage2D estimateTarget;
layout(set = 3, binding = 4, rgba32ui) uniform uimage2D denoiseSource;
layout(set = 3, binding = 5, rgba32ui) uniform uimage2D denoiseDestination;
layout(set = 3, binding = 6, rgba16f) uniform image2D occlusionSource;  // half: the bounce source at pixel 2t
// half: x the bits of the texel's view depth, y the octahedral world geometric normal at pixel 2t, the normals target's ba
// as packSnorm2x16 packs it (OcclusionImages.DepthAndNormal)
layout(set = 3, binding = 7, rg32ui) uniform uimage2D occlusionDepthAndNormal;
// full: rgb the diffuse part of each lit pixel's direct light, pre-exposed (RenderTargets.Diffuse)
layout(set = 3, binding = 8, rgba16f) uniform readonly image2D diffuseImage;
// half: the continuation flags of each texel's plane, bits 0 to 3 for the left, upper, right and lower neighbor
// (continuationFlags in texel_surface.glsl, OcclusionImages.Continuation)
layout(set = 3, binding = 9, r8ui) uniform uimage2D occlusionContinuation;

layout(push_constant, std430) uniform OcclusionPush
{
    float radiusMeters;
    uint slices;
    uint stepsPerSide;
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

// A view-space direction in world space: the inverse of toViewAxes, whose axes are orthonormal.
vec3 fromViewAxes(vec3 view)
{
    vec2 tangents = cameraTangents();
    return view.x * (frame.cameraRight.xyz / tangents.x) + view.y * (frame.cameraUp.xyz / tangents.y)
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

// The card code of a full-resolution pixel that shades as hair, NoCard for another model (docs/plans/skin-and-hair.md,
// "The shading target").
uint cardAt(ivec2 pixel)
{
    uvec4 shading = texelFetch(gbufferShading, pixel, 0);
    return cardOfWords(shading.b, shading.a);
}

// Writes the depth pass's copy of a half-resolution texel (texelDepthAndNormal in texel_surface.glsl): the view depth z
// and the octahedral geometric normal of its representative pixel in one texel, which a near step reads in one load.
void storeDepthAndNormal(ivec2 texel, uvec2 depthAndNormal)
{
    imageStore(occlusionDepthAndNormal, texel, uvec4(depthAndNormal, 0u, 0u));
}

// The depth pass's copy of a half-resolution texel inside the image (storeDepthAndNormal).
uvec2 depthAndNormalAt(ivec2 texel)
{
    return imageLoad(occlusionDepthAndNormal, texel).xy;
}

// The view depth that a copy of depthAndNormalAt holds, the very float of the view depth image.
float depthOf(uvec2 depthAndNormal)
{
    return uintBitsToFloat(depthAndNormal.x);
}

// The view-space geometric normal that a copy of depthAndNormalAt holds. unpackSnorm2x16 gives back the SNORM16 values
// of the normals target, as a read of an RG16 SNORM image does.
vec3 geometricNormalOf(uvec2 depthAndNormal)
{
    return toViewAxes(octDecode(unpackSnorm2x16(depthAndNormal.y)));
}

// Writes the depth pass's continuation flags of a half-resolution texel (continuationFlags in texel_surface.glsl).
void storeContinuation(ivec2 texel, uint flags)
{
    imageStore(occlusionContinuation, texel, uvec4(flags));
}

// The continuation flags of a half-resolution texel inside the image (storeContinuation).
uint continuationAt(ivec2 texel)
{
    return imageLoad(occlusionContinuation, texel).r;
}

// The view depth of a half-resolution texel. A texel outside the image reads the border texel, as XeGTAO's clamped
// sampler does.
float viewDepthAt(ivec2 texel)
{
    return imageLoad(viewDepth, clamp(texel, ivec2(0), halfSize() - 1)).r;
}

#endif
