// The lit surface that the G-buffer holds at a pixel (docs/plans/screen-space-reflections.md, "The one place where SSR
// reads the ambient light"). The lighting pass and the reflections' passes read a lit pixel's surface only through it,
// and the occlusion of ambient occlusion enters the shading input here (docs/plans/ambient-occlusion.md). So does the
// reflection's footprint, which the lighting pass and the pass that adds the reflections' delta (the full-resolution
// trace, or the upsample at half resolution) take from the G-buffer, so that the delta takes away the very ambient
// specular term that the lighting pass added (docs/plans/environment-map.md, "Where it runs"). The pixel must lie
// inside the G-buffer: texelFetch outside an image is undefined.
#ifndef TERTIUS_GBUFFER_SURFACE_GLSL
#define TERTIUS_GBUFFER_SURFACE_GLSL

#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"
#include "lighting.glsl"
#include "screen.glsl"

// d at a pixel: a surface's specular dominant direction toward the eye, and a sky pixel's view ray.
vec3 dominantDirectionAt(ivec2 pixel)
{
    vec3 view = normalize(viewRayThrough(vec2(pixel) + 0.5));
    if (texelFetch(gbufferDistance, pixel, 0).r <= 0.0)
        return view;
    vec3 normal = octDecode(texelFetch(gbufferNormal, pixel, 0).rg);
    return ambientSpecularDirection(normal, texelFetch(gbufferSpecular, pixel, 0).g, -view);
}

// f, in rad: how far d moves to the next pixel along x and along y, at least p, the main view's pixel angle. In the
// image's last column or row, the step goes back one pixel.
float reflectionFootprintAt(ivec2 pixel)
{
    ivec2 last = textureSize(gbufferDistance, 0) - 1;
    int nextX = pixel.x < last.x ? pixel.x + 1 : max(pixel.x - 1, 0);
    int nextY = pixel.y < last.y ? pixel.y + 1 : max(pixel.y - 1, 0);
    vec3 d = dominantDirectionAt(pixel);
    float alongX = length(dominantDirectionAt(ivec2(nextX, pixel.y)) - d);
    float alongY = length(dominantDirectionAt(ivec2(pixel.x, nextY)) - d);
    return max(frame.params.z, max(alongX, alongY));
}

ShadingInput surfaceAt(ivec2 pixel)
{
    ShadingInput s;
    s.baseColor = texelFetch(gbufferBaseColor, pixel, 0).rgb;
    vec4 normals = texelFetch(gbufferNormal, pixel, 0);
    s.normal = octDecode(normals.rg);
    s.geometricNormal = octDecode(normals.ba);
    vec4 specular = texelFetch(gbufferSpecular, pixel, 0);
    s.metallic = specular.r;
    s.roughness = specular.g;
    s.reflectance = specular.b;
    s.lobeSkyFraction = specular.a;
    s.skyFraction = texelFetch(gbufferEmissive, pixel, 0).a;
    s.reflectionFootprint = reflectionFootprintAt(pixel);
    s.occlusion = texelFetch(gbufferOcclusion, pixel, 0).r;
    return s;
}

#endif
