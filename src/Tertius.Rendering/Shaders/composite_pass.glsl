// The composite (https://github.com/alexispurslane/tertius/wiki/temporal-renderer, section 9, "The composite"): at each lit pixel, the light of section 1
// into the HDR image, capped at MaxRadiance. It multiplies the direct light's two images and the two indirect terms back
// by their material factors and adds emission. Glossy surfaces blend their reflection history with the environment.
// Under the scatter it also writes the diffuse image, the diffuse factor times the two diffuse terms and the skin mask at
// skin, and 0 elsewhere (https://github.com/alexispurslane/tertius/wiki/skin-and-hair, "The diffuse light D"). composite.frag and composite_scatter.frag,
// which defines SCATTER_OUTPUT, include it: the diffuse image is a second color attachment only then. A test's probe
// defines TERTIUS_DIRECT_LIGHT_ONLY, which leaves out the ambient light and the sky: the direct light and the emission.
// With TERTIUS_DIRECT_DIFFUSE_ONLY too, it writes the diffuse part alone.
#ifndef TERTIUS_COMPOSITE_PASS_GLSL
#define TERTIUS_COMPOSITE_PASS_GLSL

#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "temporal.glsl"

// Set 3 of the composite layout, the light set owned by CompositePass: the direct light's two parts, and the slow
// histories of the indirect diffuse and indirect specular signals, rgb the light, demodulated and pre-exposed. Under
// NoScreenTrace, bindings 2 and 3 hold the direct images, which the composite does not read.
layout(set = 3, binding = 0, rgba16f) uniform readonly image2D directDiffuseLight;
layout(set = 3, binding = 1, rgba16f) uniform readonly image2D directSpecularLight;
#include "specular_history.glsl"
layout(set = 3, binding = 2, SPECULAR_HISTORY_FORMAT) uniform readonly image2D indirectSpecularLight;

#include "reflection_sharing_prototype.glsl"

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outColor;
#ifdef SCATTER_OUTPUT
// The diffuse image: the pass writes every one of its pixels.
layout(location = 1) out vec4 outDiffuse;
#endif

#ifdef LIGHT_VOLUME
#include "volume_receive.glsl"
#endif

void writeDiffuse(vec4 value)
{
#ifdef SCATTER_OUTPUT
    outDiffuse = value;
#endif
}

void main()
{
    // A helper invocation of a 2 × 2 quad can lie past the image's last column or row when the size is odd, and a fetch
    // there is undefined, so every read stays inside the image.
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), ivec2(frame.viewport.xy) - 1);
    vec3 direction = normalize(inRay);
    if (texelFetch(gbufferDistance, pixel, 0).r <= 0.0)
    {
#ifdef TERTIUS_DIRECT_LIGHT_ONLY
        outColor = vec4(0.0);
#else
        outColor = vec4(preExposed(skyRadiance(direction) + starRadiance(direction)), 1.0);
#endif
        writeDiffuse(vec4(0.0));
        return;
    }

    // The G-buffer pass pre-exposed the emission.
    vec3 emissive = texelFetch(gbufferEmissive, pixel, 0).rgb;
    // An impostor's light is baked into its emission, ambient light included.
    if (texelFetch(gbufferBaseColor, pixel, 0).a == 0.0)
    {
#ifdef TERTIUS_DIRECT_DIFFUSE_ONLY
        outColor = vec4(0.0);
#else
        outColor = vec4(emissive, 1.0);
#endif
        writeDiffuse(vec4(0.0));
        return;
    }

    ShadingInput s = materialAt(pixel);
    vec3 V = -direction;
    SurfaceLobes lobes = lobesOf(s, V);
    MaterialFactors factors = materialFactors(s, lobes);
    vec3 directDiffuse = factors.diffuse * imageLoad(directDiffuseLight, pixel).rgb;
    vec3 directSpecular = factors.specular * imageLoad(directSpecularLight, pixel).rgb;
#if defined(TERTIUS_DIRECT_DIFFUSE_ONLY)
    outColor = vec4(min(directDiffuse, vec3(MaxRadiance)), 1.0);
    return;
#elif defined(TERTIUS_DIRECT_LIGHT_ONLY)
    outColor = vec4(min(directDiffuse + directSpecular + emissive, vec3(MaxRadiance)), 1.0);
    return;
#endif
    IndirectLight indirect = closedForm(withAmbientFootprint(s, pixel), V);
    vec3 indirectDiffuse = factors.diffuse * indirect.diffuse;
    vec3 indirectSpecular = factors.specular * indirect.specular;
#ifdef LIGHT_VOLUME
    applyLightVolume(s, V, factors, indirectDiffuse, indirectSpecular);
#endif
    float share = glossyShare(s.roughness);
    if (screenTraceOn && share > 0.0)
    {
        vec3 glossy = factors.specular * imageLoad(indirectSpecularLight, pixel).rgb;
        indirectSpecular = mix(indirectSpecular, reflectionSharedAt(pixel, glossy), share);
    }
    // Each part is finite and at most a few times MaxRadiance, so the sum is finite, and the cap keeps the surfaces' share
    // (https://github.com/alexispurslane/tertius/wiki/screen-space-illumination, "The cap at MaxRadiance").
    outColor = vec4(
        min(directDiffuse + directSpecular + indirectDiffuse + indirectSpecular + emissive, vec3(MaxRadiance)), 1.0);
    // D, at most MaxRadiance, and the mask, which marks the skin pixels that alone the scatter passes spread.
    bool skin = s.model == ShadingSkin;
    writeDiffuse(skin ? vec4(min(directDiffuse + indirectDiffuse, vec3(MaxRadiance)), 1.0) : vec4(0.0));
}

#endif
