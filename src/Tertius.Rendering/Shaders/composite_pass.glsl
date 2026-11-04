// The composite (docs/plans/temporal-renderer.md, section 9, "The composite"): at each lit pixel, the light of section 1
// into the HDR image, capped at MaxRadiance. It multiplies the direct signal's slow histories back by their material
// factors (decision 18), adds today's ambient terms with the occlusion passes' estimate until the screen tracer replaces
// them, and adds the emission. The sky's pixels get the sky and the stars, and an impostor's pixels their baked light.
// Under the scatter it also writes the diffuse image, the diffuse factor times the two diffuse terms and the skin mask at
// skin, and 0 elsewhere (docs/plans/skin-and-hair.md, "The diffuse light D"). composite.frag and composite_scatter.frag,
// which defines SCATTER_OUTPUT, include it: the diffuse image is a second color attachment only then. A test's probe
// defines TERTIUS_DIRECT_LIGHT_ONLY, which leaves out the ambient light and the sky: the direct light and the emission, as
// the direct image held them before the direct signal. With TERTIUS_DIRECT_DIFFUSE_ONLY too, it writes the diffuse part
// alone, as the diffuse image held it.
#ifndef TERTIUS_COMPOSITE_PASS_GLSL
#define TERTIUS_COMPOSITE_PASS_GLSL

#include "frame.glsl"
#include "gbuffer_surface.glsl"
#include "temporal.glsl"

// Set 3 of the composite layout, the signal read set: the direct signal's slow histories, rgb the light, demodulated and
// pre-exposed, a the history length (TemporalSignal.ReadSet).
layout(set = 3, binding = 0, rgba16f) uniform readonly image2D directDiffuseHistory;
layout(set = 3, binding = 1, rgba16f) uniform readonly image2D directSpecularHistory;

layout(location = 0) in vec3 inRay;
layout(location = 0) out vec4 outColor;
#ifdef SCATTER_OUTPUT
// The diffuse image: the pass writes every one of its pixels.
layout(location = 1) out vec4 outDiffuse;
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
    ivec2 pixel = min(ivec2(gl_FragCoord.xy), textureSize(gbufferDistance, 0) - 1);
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

    ShadingInput s = ambientSurfaceAt(pixel);
    vec3 V = -direction;
    SurfaceLobes lobes = lobesOf(s, V);
    MaterialFactors factors = materialFactors(s, lobes);
    vec3 directDiffuse = factors.diffuse * imageLoad(directDiffuseHistory, pixel).rgb;
    vec3 directSpecular = factors.specular * imageLoad(directSpecularHistory, pixel).rgb;
#if defined(TERTIUS_DIRECT_DIFFUSE_ONLY)
    outColor = vec4(min(directDiffuse, vec3(MaxRadiance)), 1.0);
    return;
#elif defined(TERTIUS_DIRECT_LIGHT_ONLY)
    outColor = vec4(min(directDiffuse + directSpecular + emissive, vec3(MaxRadiance)), 1.0);
    return;
#endif
    AmbientTerms ambient = ambientTerms(s, lobes, V);
    float k = exposureScale();
    // Each part is finite and at most a few times MaxRadiance, so the sum is finite, and the cap keeps the surfaces' share
    // (docs/plans/screen-space-illumination.md, "The cap at MaxRadiance").
    vec3 ambientLight = exposed(ambient.diffuse + ambient.specular, k);
    outColor = vec4(min(directDiffuse + directSpecular + ambientLight + emissive, vec3(MaxRadiance)), 1.0);
    // D, at most MaxRadiance, and the mask, which marks the skin pixels that alone the scatter passes spread.
    bool skin = s.model == ShadingSkin;
    writeDiffuse(skin ? vec4(min(directDiffuse + exposed(ambient.diffuse, k), vec3(MaxRadiance)), 1.0) : vec4(0.0));
}

#endif
