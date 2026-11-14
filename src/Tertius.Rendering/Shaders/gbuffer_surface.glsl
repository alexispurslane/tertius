// The lit surface that the G-buffer holds at a pixel. The direct pass, the composite, the screen tracer and the
// accumulator read a lit pixel's surface only through it. So does the reflection's footprint, which the closed form of
// the environment's specular term reads from the G-buffer (docs/plans/environment-map.md, "Where it runs"), only where
// that term reads it (withAmbientFootprint). The pixel must lie inside the G-buffer: texelFetch outside an image is
// undefined.
#ifndef TERTIUS_GBUFFER_SURFACE_GLSL
#define TERTIUS_GBUFFER_SURFACE_GLSL

#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"
#include "lighting.glsl"
#include "exposure.glsl"
#include "screen.glsl"

// d at a pixel: a surface's specular dominant direction toward the eye, and a sky pixel's view ray.
vec3 dominantDirectionAt(ivec2 pixel)
{
    vec3 view = normalize(viewRayThrough(vec2(pixel) + 0.5));
    if (texelFetch(gbufferDistance, pixel, 0).r <= 0.0)
        return view;
    vec3 normal = octDecode(texelFetch(gbufferNormal, pixel, 0).rg);
    return ambientSpecularDirection(normal, specularAt(pixel).g, -view);
}

// f, in rad: how far d moves to a neighboring pixel along x and along y, at least p, the main view's pixel angle. Along
// each axis, the smaller of the steps to the next and to the previous pixel, so that a crease, a seam or a silhouette on
// one side leaves f at the rate of the pixel's own surface. In the image's first or last column or row, both steps go
// to the one neighbor there.
float reflectionFootprintAt(ivec2 pixel)
{
    ivec2 last = ivec2(frame.viewport.xy) - 1;
    ivec2 next = ivec2(pixel.x < last.x ? pixel.x + 1 : max(pixel.x - 1, 0),
        pixel.y < last.y ? pixel.y + 1 : max(pixel.y - 1, 0));
    ivec2 previous = ivec2(pixel.x > 0 ? pixel.x - 1 : min(pixel.x + 1, last.x),
        pixel.y > 0 ? pixel.y - 1 : min(pixel.y + 1, last.y));
    vec3 d = dominantDirectionAt(pixel);
    float alongX = min(length(dominantDirectionAt(ivec2(next.x, pixel.y)) - d),
        length(dominantDirectionAt(ivec2(previous.x, pixel.y)) - d));
    float alongY = min(length(dominantDirectionAt(ivec2(pixel.x, next.y)) - d),
        length(dominantDirectionAt(ivec2(pixel.x, previous.y)) - d));
    return max(frame.params.z, max(alongX, alongY));
}

// The surface, with the footprint at its floor p: the direct light reads no footprint, and withAmbientFootprint sets it
// where the closed form reads it.
ShadingInput materialAt(ivec2 pixel)
{
    ShadingInput s;
    s.position = normalize(viewRayThrough(vec2(pixel) + 0.5)) * texelFetch(gbufferDistance, pixel, 0).r;
    vec4 baseColor = texelFetch(gbufferBaseColor, pixel, 0);
    s.baseColor = baseColor.rgb;
    s.map = mapOfCode(baseColor.a);
    vec4 normals = texelFetch(gbufferNormal, pixel, 0);
    s.normal = octDecode(normals.rg);
    s.geometricNormal = octDecode(normals.ba);
    vec4 specular = specularAt(pixel);
    s.metallic = specular.r;
    s.roughness = specular.g;
    s.reflectance = specular.b;
    s.lobeSkyFraction = specular.a;
    s.skyFraction = texelFetch(gbufferEmissive, pixel, 0).a;
    s.reflectionFootprint = frame.params.z;
    uvec4 shading = texelFetch(gbufferShading, pixel, 0);
    s.model = modelOfWord(shading.a);
    s.strand = s.model == ShadingHair ? strandOfCode(shading.rg) : vec3(0.0);
    return s;
}

// Whether the ambient light reads the surface's footprint: only the map's reflected stars read it, and only where the
// lobe alone does not end their fade (lobeEndsStarFade).
bool ambientReadsFootprint(ShadingInput s)
{
    float roughness = flooredRoughness(s.roughness);
    return !skipsTermsThatAddNothing || environmentMapOn && !lobeEndsStarFade(roughness * roughness);
}

// The surface s of the pixel for the ambient light: with the footprint f where the ambient light reads it, and with it
// left at p elsewhere, which gives the same light and saves the G-buffer reads of reflectionFootprintAt.
ShadingInput withAmbientFootprint(ShadingInput s, ivec2 pixel)
{
    if (ambientReadsFootprint(s))
        s.reflectionFootprint = reflectionFootprintAt(pixel);
    return s;
}

#endif
