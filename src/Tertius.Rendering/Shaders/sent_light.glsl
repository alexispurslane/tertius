// The fresh light that a pixel sends toward a reflection ray (ARCHITECTURE.md, "Surface share"): the light that this draw
// computed for the pixel's surface before any history, times the pixel's surface share. Its two readers include it, the
// plan's hit light (screen_trace.glsl, leavingLightOf) and the guide's finest level (light_pyramid_finest.comp), so that
// the guide aims by the light that a hit reads. The share needs the pixel's surface reprojection, so this file also gives
// it to every reader of a pixel's last place: lastPlaceAt and surfaceReprojectionAt, which the plan's light age and the
// tracer's specular reprojection call too. Needs frame.glsl, gbuffer_surface.glsl and signals.glsl, and the
// includer's two storage images of the surface test pass: surfaceTest, the pass bits (r16ui), and motionTarget, the
// G-buffer's motion target (rgba16f).
#ifndef TERTIUS_SENT_LIGHT_GLSL
#define TERTIUS_SENT_LIGHT_GLSL

// The pixel's place in the last frame's image, from its motion.
vec2 lastPlaceAt(ivec2 pixel)
{
    return readPlace(vec2(pixel) + 0.5, imageLoad(motionTarget, pixel));
}

// The pixel's diffuse reprojection into the last frame: its last place's taps with their surface pass bits.
Reprojection surfaceReprojectionAt(ivec2 pixel)
{
    return reprojectionOf(lastPlaceAt(pixel), surfacePassBits(imageLoad(surfaceTest, pixel).r));
}

// The glow of the pixel's surface: its emission, or 0 where its entity holds a light. An impostor's glow holds all of
// its light.
vec3 glowAt(ivec2 pixel)
{
    return glowSent(preExposed(texelFetch(gbufferEmissive, pixel, 0).rgb), lightMarkOfWord(texelFetch(gbufferShading, pixel, 0).b));
}

// The fresh diffuse light that the pixel q sends: its glow and directDiffuse, its direct diffuse light times its diffuse
// factor (0 for an impostor), times the surface share of its diffuse reprojection `surface`.
vec3 sentDiffuseLight(ivec2 q, vec3 directDiffuse, Reprojection surface)
{
    return surfaceShare(surface) * (glowAt(q) + directDiffuse);
}

#endif
