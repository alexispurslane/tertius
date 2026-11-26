"""Temporary CPU readback of existing raw specular samples and raster material bytes."""
from pathlib import Path

RENDERER = Path('src/Tertius.Rendering/VulkanRenderer.cs')
NEEDLE = '        LastFrameStats = StatsOf(\n'
INSERT = '''        if (sink != null && Environment.GetEnvironmentVariable("TERTIUS_ISSUE5_RAW") is { } rawPath
            && screenTrace is { } probeTracer)
        {
            Directory.CreateDirectory(rawPath);
            GpuImage probeImage = probeTracer.Specular.Sample(0);
            float[] probeValues = ReadImage(probeImage);
            string probeName = Path.Combine(rawPath, history.DrawnFrames.ToString("D6"));
            File.WriteAllBytes(probeName + ".sample.f32", System.Runtime.InteropServices.MemoryMarshal.AsBytes(probeValues.AsSpan()).ToArray());
            File.WriteAllBytes(probeName + ".material.u8", ReadSpecularTarget());
            File.WriteAllText(Path.Combine(rawPath, "size.txt"), $"{probeImage.Extent.Width} {probeImage.Extent.Height}");
        }

'''


def patched(original):
    assert original.count(NEEDLE) == 1
    return original.replace(NEEDLE, INSERT + NEEDLE)

ACCUMULATE = Path('src/Tertius.Rendering/Shaders/accumulate_temporal.comp')


def source_resolve(original):
    before = '    vec3 specularLight = vec3(0.0);'
    assert original.count(before) == 1
    original = original.replace(before, before + '\n    vec3 probeDiffuseSpecular = vec3(0.0);')
    before = '        specularLight = handOver * diffuse.light.rgb * specularWeightOf(p, lobes, factors, diffuseRay);'
    assert original.count(before) == 1
    original = original.replace(before, before + '\n        probeDiffuseSpecular = specularLight;')
    before = '    imageStore(uncoveredDiagnostic, pixel, vec4(float(seenCode) - 1.0, 0.0, 0.0, 0.0));'
    assert original.count(before) == 1
    return original.replace(before, '    imageStore(uncoveredDiagnostic, pixel, vec4(probeDiffuseSpecular, handOver));')


def source_accumulate(original):
    before = '''    imageStore(uncoveredDiagnostic, pixel, vec4(imageLoad(uncoveredDiagnostic, pixel).r,
        hasSample ? historyLength : -1.0, holdsCutShare ? 1.0 : 0.0, historyLength));'''
    assert original.count(before) == 1
    return original.replace(before, '    // The research probe retains the resolve diagnostics without changing lighting.')
