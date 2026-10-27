// The direct pass (docs/plans/screen-space-illumination.md, "The direct pass"): the light of each light of the list
// that the main view keeps, plus the emission, pre-exposed and capped, into the direct image. No ambient light: the
// ambient pass adds it after the occlusion passes. When the targets hold the diffuse image, under the skin scatter or the
// screen-space light, it also writes the diffuse part and the skin mask into it (docs/plans/skin-and-hair.md, "The
// diffuse light D"). direct.frag and direct_diffuse.frag, which defines DIFFUSE_OUTPUT, include it: the diffuse image is
// a second color attachment only then, and an output with no attachment is a write that validation reports.
#ifndef TERTIUS_DIRECT_PASS_GLSL
#define TERTIUS_DIRECT_PASS_GLSL

#include "frame.glsl"
#include "gbuffer_surface.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outColor;
#ifdef DIFFUSE_OUTPUT
// The diffuse image: the pass writes every one of its pixels.
layout(location = 1) out vec4 outDiffuse;
#endif

void writeDiffuse(vec4 value)
{
#ifdef DIFFUSE_OUTPUT
    outDiffuse = value;
#endif
}

void main()
{
    // A helper invocation of a 2 × 2 quad can lie past the image's last column or row when the size is odd, and a fetch
    // there is undefined, so every read stays inside the image.
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), textureSize(gbufferDistance, 0) - 1);
    vec3 direction = normalize(inRay);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    // Sky: no reader reads the direct image there, and the ambient pass draws the sky and the stars.
    if (distanceToEye <= 0.0)
    {
        outColor = vec4(0.0);
        writeDiffuse(vec4(0.0));
        return;
    }

    // The G-buffer pass pre-exposed the emission.
    vec3 emissive = texelFetch(gbufferEmissive, pixel, 0).rgb;
    // Base color alpha 0 marks an unlit pixel: an impostor, whose light, ambient light included, is baked into its
    // emission.
    if (texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
    {
        outColor = vec4(emissive, 1.0);
        writeDiffuse(vec4(0.0));
        return;
    }

    // The position comes from the ray and the stored distance, so it does not depend on the projection of the
    // depth range that drew the pixel.
    vec3 position = direction * distanceToEye;
    vec3 V = -direction;
    ShadingInput s = materialAt(pixel);
    DirectTerms direct = directLight(s, lobesOf(s, V), position, V, ShadowMapsMainView, MainView);
    float k = exposureScale();
    outColor = vec4(min(exposed(directSum(direct), k) + emissive, vec3(MaxRadiance)), 1.0);
    // The diffuse part alone, at most MaxRadiance: the light that skin scatters and that the bounce light sends on. The
    // mask marks skin, whose pixels alone the scatter passes spread.
    writeDiffuse(vec4(exposed(direct.diffuse, k), s.model == ShadingSkin ? 1.0 : 0.0));
}

#endif
