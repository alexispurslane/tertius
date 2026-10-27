// The ambient pass (docs/plans/screen-space-illumination.md, "The ambient pass"): the direct image plus the ambient light
// with the estimate applied, capped, into the HDR image, and the sky and the stars on sky pixels. Under the scatter, it
// also adds D's ambient part to the diffuse image at skin (docs/plans/skin-and-hair.md, "The diffuse light D").
// ambient.frag and ambient_scatter.frag, which defines SCATTER_OUTPUT, include it, as direct_pass.glsl says.
#ifndef TERTIUS_AMBIENT_PASS_GLSL
#define TERTIUS_AMBIENT_PASS_GLSL

#include "frame.glsl"
#include "gbuffer_surface.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outColor;
#ifdef SCATTER_OUTPUT
// The diffuse image. The blend adds the output's rgb and keeps the image's alpha, the mask that the direct pass wrote.
layout(location = 1) out vec4 outScatter;
#endif

void addScatter(vec3 diffuse)
{
#ifdef SCATTER_OUTPUT
    outScatter = vec4(diffuse, 0.0);
#endif
}

void main()
{
    // A helper invocation of a 2 × 2 quad can lie past the image's last column or row when the size is odd, and a fetch
    // there is undefined, so every read stays inside the image.
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), textureSize(gbufferDistance, 0) - 1);
    vec3 direction = normalize(inRay);
    if (texelFetch(gbufferDistance, pixel, 0).r <= 0.0)
    {
        outColor = vec4(preExposed(skyRadiance(direction) + starRadiance(direction)), 1.0);
        addScatter(vec3(0.0));
        return;
    }

    vec3 direct = texelFetch(gbufferDirect, pixel, 0).rgb;
    // An impostor's light is baked, ambient light included: the direct pass already wrote all of it.
    if (texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
    {
        outColor = vec4(direct, 1.0);
        addScatter(vec3(0.0));
        return;
    }

    ShadingInput s = ambientSurfaceAt(pixel);
    vec3 V = -direction;
    AmbientTerms ambient = ambientTerms(s, lobesOf(s, V), V);
    float k = exposureScale();
    // Each part is finite and at most MaxRadiance, so the sum is finite, and the cap keeps the surfaces' share
    // (docs/plans/screen-space-illumination.md, "The cap at MaxRadiance").
    outColor = vec4(min(direct + exposed(ambient.diffuse + ambient.specular, k), vec3(MaxRadiance)), 1.0);
    // D's ambient part, at most MaxRadiance, so the blended sum stays at most 2 · MaxRadiance = 64,512.
    addScatter(s.model == ShadingSkin ? exposed(ambient.diffuse, k) : vec3(0.0));
}

#endif
