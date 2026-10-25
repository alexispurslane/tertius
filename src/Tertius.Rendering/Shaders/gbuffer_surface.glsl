// The lit surface that the G-buffer holds at a pixel (docs/plans/screen-space-reflections.md, "The one place where SSR
// reads the ambient light"). The lighting pass and the reflection trace read a lit pixel's surface only through it, and
// the occlusion of ambient occlusion enters the shading input here (docs/plans/ambient-occlusion.md). The pixel must lie
// inside the G-buffer: texelFetch outside an image is undefined.
#ifndef TERTIUS_GBUFFER_SURFACE_GLSL
#define TERTIUS_GBUFFER_SURFACE_GLSL

#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"
#include "lighting.glsl"

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
    s.occlusion = texelFetch(gbufferOcclusion, pixel, 0).r;
    return s;
}

#endif
