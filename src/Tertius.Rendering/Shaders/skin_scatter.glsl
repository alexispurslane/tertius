// One pass of the separable skin scatter at one skin pixel (docs/plans/skin-and-hair.md, "The two passes"), and set 3
// of the scatter passes. SkinScatter.Pass in Shading/ is the C# copy. Change both together.
#ifndef TERTIUS_SKIN_SCATTER_GLSL
#define TERTIUS_SKIN_SCATTER_GLSL

#extension GL_EXT_samplerless_texture_functions : require

#include "frame.glsl"
#include "gbuffer_inputs.glsl"
#include "screen.glsl"
#include "subsurface.glsl"

layout(set = 3, binding = 0, rgba16f) uniform readonly image2D diffuseImage;  // a 1 at skin, else 0; rgb D at skin
layout(set = 3, binding = 1, rgba16f) uniform image2D rowImage;               // the rows pass's sum at skin, else 0
layout(set = 3, binding = 2, rgba16f) uniform image2D hdrImage;               // the lit image, at the pixel only
layout(set = 3, binding = 3) uniform ScatterKernel
{
    // Tap i of 0 to 2n: rgb its weight per channel, a its offset in m. Tap n is the center, with offset 0
    // (SkinProfile.Kernel).
    vec4 kernel[2 * MaxScatterTapsPerSide + 1];
    // ScatterKernel.WidthScale: how many times wider than the measured profile the offsets are. The slope rule divides a
    // tap's offset by it. GpuScatterKernel in Scene/GpuTypes.cs is the C# copy of this block. Change both together.
    float kernelWidthScale;
};

const float SlopeLimit = 1.7320508075688772;  // tan 60°, per meter of the measured profile
// The largest image side. A tap's offset is clamped to it as a float before its conversion to an integer, which would
// overflow for a tap far past the image.
const float MaxSide = 16384.0;

// z at a pixel: its distance along the camera's forward axis, from the distance target and the view ray, whose
// forward component is 1. A skin pixel's distance is above 0.
float viewDepthAt(ivec2 pixel)
{
    return texelFetch(gbufferDistance, pixel, 0).r / length(viewRayThrough(vec2(pixel) + 0.5));
}

// The pass's source at a tap: the diffuse image for the rows, the row image for the columns.
vec3 sourceAt(ivec2 pixel, ivec2 axis)
{
    return axis.x != 0 ? imageLoad(diffuseImage, pixel).rgb : imageLoad(rowImage, pixel).rgb;
}

// The kernel's sum along axis at the skin pixel, whose source holds center. Each tap reads the one pixel nearest it. A
// tap gives way to the center's light where its pixel is not skin, and where the depth rises past the slope limit.
vec3 scatterAlong(ivec2 pixel, ivec2 axis, vec3 center)
{
    float depth = viewDepthAt(pixel);
    // s(z) = H / (2 z tan(φ_y / 2)): length(cameraUp) is the tangent of the vertical half angle.
    float pixelsPerMeter = frame.viewport.y / (2.0 * depth * length(frame.cameraUp.xyz));
    // The outermost tap's offset is the kernel's reach (ScatterKernel.Reach).
    if (kernel[2 * scatterTapsPerSide].a * pixelsPerMeter < 0.5)
        return center;
    ivec2 last = imageSize(diffuseImage) - 1;
    vec3 sum = vec3(0.0);
    for (int i = 0; i <= 2 * scatterTapsPerSide; i++)
    {
        vec4 tap = kernel[i];
        float offset = clamp(floor(tap.a * pixelsPerMeter + 0.5), -MaxSide, MaxSide);
        ivec2 at = clamp(pixel + axis * int(offset), ivec2(0), last);
        vec3 light = center;
        if (i != scatterTapsPerSide && imageLoad(diffuseImage, at).a > 0.0)
        {
            // Up to the slope limit the tap keeps its own light, and up to twice the limit it gives way to the center's.
            float run = abs(tap.a) / kernelWidthScale;
            float rise = abs(viewDepthAt(at) - depth) / (run * SlopeLimit);
            light = mix(center, sourceAt(at, axis), clamp(2.0 - rise, 0.0, 1.0));
        }
        sum += tap.rgb * light;
    }
    return sum;
}

// Whether the pixel lies inside the images: the dispatch rounds the image up to whole workgroups.
bool insideScatter(ivec2 pixel)
{
    return all(lessThan(pixel, imageSize(diffuseImage)));
}

#endif
