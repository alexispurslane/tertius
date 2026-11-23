# Tool code of ter-8pco (the ray census). Applies the census edits to a build copy that already holds
# tools/renderer-survey/probe.patch: the resolve writes the diffuse sample's class into its alpha, and the renderer
# hands each drawn frame to RayCensus. The edits never enter src. Usage: python3 -I apply-census.py <build copy root>
import os, shutil, sys

root = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))


def edit(rel, old, new):
    path = os.path.join(root, rel)
    s = open(path).read()
    assert s.count(old) == 1, (rel, old[:60])
    open(path, 'w').write(s.replace(old, new))


resolve = 'src/Tertius.Rendering/Shaders/screen_trace_resolve.comp'
edit(resolve,
     """        imageStore(diffuseSample, pixel, vec4(0.0));
        imageStore(specularSample, pixel, vec4(0.0));
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
        imageStore(specularSampleAge, pixel, uvec4(0u));
#endif
#ifdef TERTIUS_LIGHT_AGE_DIAGNOSTIC""",
     """        // PROBE (ter-8pco): -1 in alpha marks a pixel without a traced surface for the census.
        imageStore(diffuseSample, pixel, vec4(0.0, 0.0, 0.0, -1.0));
        imageStore(specularSample, pixel, vec4(0.0));
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
        imageStore(specularSampleAge, pixel, uvec4(0u));
#endif
#ifdef TERTIUS_LIGHT_AGE_DIAGNOSTIC""")
edit(resolve,
     """    imageStore(diffuseSample, pixel, vec4(min(diffuseLight, vec3(MaxRadiance)), 1.0));""",
     """    // PROBE (ter-8pco, the ray census): alpha holds the sample's hit-distance bin plus a sixteenth of log2 of its
    // pre-cap luminance over its luminance (capped at 15). Bin 0 is a miss (the record's hit distance is 0); a hit at
    // distance d takes bin clamp(floor(2 log2 d) + 21, 1, 63), half an octave wide, so that 2 m is an edge: bins up to 22
    // lie below 2 m and bins from 23 at or beyond it. No pass reads the diffuse sample's alpha, so the image's rgb and
    // the frame are unchanged.
    float censusDistance = diffuse.light.a;
    float censusBin = censusDistance <= 0.0 ? 0.0 : clamp(floor(2.0 * log2(censusDistance)) + 21.0, 1.0, 63.0);
    vec3 censusLuminance = vec3(0.2126, 0.7152, 0.0722);
    float censusRawWeight = max(dot(p.normal, diffuseRay.direction), 0.0) / max(PI * diffuseRay.density, LeastRayCosine);
    float censusCapped = dot(min(diffuseLight, vec3(MaxRadiance)), censusLuminance);
    float censusRaw = diffuseRay.brings ? dot(diffuse.light.rgb, censusLuminance) * censusRawWeight : 0.0;
    float censusRatio = censusCapped > 0.0 && censusRaw > censusCapped ? min(log2(censusRaw / censusCapped), 15.0) : 0.0;
    imageStore(diffuseSample, pixel, vec4(min(diffuseLight, vec3(MaxRadiance)), censusBin + censusRatio / 16.0));""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """        CommitDrawnFrame(frame: frame, snapshot: snapshot, pending: pending);
""",
     """        CommitDrawnFrame(frame: frame, snapshot: snapshot, pending: pending);
        // PROBE (ter-8pco, never commit to src): the ray census reads this draw's diffuse samples back.
        if (RayCensus.On)
            RayCensus.Observe(this, Exposure.Latest?.Stops, pending.RenderSize.Width, pending.RenderSize.Height);
""")
edit('src/Tertius.Rendering/VulkanRenderer.cs',
     """    private (float[] Values, int Width, int Height) ReadLevel(GpuImage image, int layer, int mip)
""",
     """    // PROBE (ter-8pco, never commit to src): the census's read of any level of any layer.
    internal (float[] Values, int Width, int Height) CensusReadLevel(GpuImage image, int layer, int mip) =>
        ReadLevel(image: image, layer: layer, mip: mip);

    private (float[] Values, int Width, int Height) ReadLevel(GpuImage image, int layer, int mip)
""")
shutil.copy(os.path.join(here, 'RayCensus.cs'), os.path.join(root, 'src/Tertius.Rendering/Scene/RayCensus.cs'))
print('census edits applied to', root)
