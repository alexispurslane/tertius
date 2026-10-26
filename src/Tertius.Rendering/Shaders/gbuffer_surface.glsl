// The lit surface that the G-buffer holds at a pixel (docs/plans/screen-space-reflections.md, "The one place where SSR
// reads the ambient light"). The direct and ambient passes and the reflection trace read a lit pixel's surface only
// through it, and the estimate of the light around the pixel enters the shading input here
// (docs/plans/screen-space-illumination.md, "The ambient pass"). The pixel must lie inside the G-buffer: texelFetch
// outside an image is undefined.
#ifndef TERTIUS_GBUFFER_SURFACE_GLSL
#define TERTIUS_GBUFFER_SURFACE_GLSL

#include "gbuffer_inputs.glsl"
#include "octahedral.glsl"
#include "lighting.glsl"
#include "exposure.glsl"

// The surface without the estimate, with V = 1 and G = 0, for the direct light, which reads neither. The direct pass
// reads the surface only through it, so it never reads the estimate target, which the occlusion passes write after it.
ShadingInput materialAt(ivec2 pixel)
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
    s.occlusion = 1.0;
    s.bounce = vec3(0.0);
    return s;
}

ShadingInput surfaceAt(ivec2 pixel)
{
    ShadingInput s = materialAt(pixel);
    vec4 estimate = texelFetch(gbufferEstimate, pixel, 0);
    s.occlusion = estimate.a;
    // The images hold the bounce pre-exposed. k = exp2(stops) is a normal float above 0, and the quotient stays below
    // c · 2⁶⁴, a finite single float.
    s.bounce = estimate.rgb / exposureScale();
    return s;
}

#endif
