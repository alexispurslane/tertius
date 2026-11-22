# Tool code of t0-acxm. mutants.py <repository> <out.json>: writes the tools/mutate.py list of the remedy
# (mutants/list.json), with each mutant's line found from its original text.
import json, sys
R = sys.argv[1]
P = 'tests/Tertius.Rendering.Tests'
RD = '*+RegionDrift'
AR = 'AccumulatorRenderTests'
G = 'src/Tertius.Rendering/Shaders/region_drift.glsl'
SP = 'src/Tertius.Rendering/Shaders/accumulate_spatial.comp'
TS = 'src/Tertius.Rendering/Frame/TemporalSignal.cs'
GT = 'src/Tertius.Rendering/Scene/GpuTypes.cs'
DL = 'src/Tertius.Rendering/Frame/DescriptorLayouts.cs'
m = [
 ('R01', G, 'return all(equal(block % AnchorStepBlocks', 'return any(equal(block % AnchorStepBlocks', RD),
 ('R02', G, 'RegionRingDraws - 1u) % RegionRingDraws)', 'RegionRingDraws - 2u) % RegionRingDraws)', RD),
 ('R03', G, 'if (parameters.reset != 0u || candidate >= RegionCandidates)', 'if (candidate >= RegionCandidates)', RD),
 ('R04', G, 'int(candidate % 3u)', 'int(candidate % 2u)', RD),
 ('R05', G, '(block - RegionRadiusBlocks + AnchorStepBlocks - 1) / AnchorStepBlocks', '(block - RegionRadiusBlocks) / AnchorStepBlocks', RD),
 ('R06', G, 'min((block + RegionRadiusBlocks) / AnchorStepBlocks', 'min((block + RegionRadiusBlocks + 1) / AnchorStepBlocks', RD),
 ('R07', G, 'layerBefore())).b != 0.0', 'layerBefore())).b == 0.0', RD),
 ('R08', G, 'if (!insideImages(pixel))\n        return vec3(0.0);', 'if (!insideImages(pixel))\n        return vec3(0.0, 0.0, 1.0);', RD),
 ('R09', G, 'bool counts = texelFetch(gbufferDistance, pixel, 0).r > 0.0\n        && finiteSample(sampled)', 'bool counts = finiteSample(sampled)', RD),
 ('R10', G, '\n        && (imageLoad(surfaceTest, pixel).r & SurfaceMovesOnScreen) == 0u;', ';', RD),
 ('R11', G, 'luminance(sampled) / exposureScale()', 'luminance(sampled)', RD),
 ('R12', G, '2.0 * sums.y >= sums.z', 'sums.y >= sums.z', RD),
 ('R13', G, 'min(validDrawsBefore + 1.0, float(RegionRingDraws))', 'validDrawsBefore + 1.0', RD),
 ('R14', G, 'if (now.validDraws < float(RegionRingDraws))', 'if (now.validDraws < float(RegionWindowDraws))', RD),
 ('R15', G, 'const float DriftSigmas = 3.0;', 'const float DriftSigmas = 2.0;', RD),
 ('R16', G, 'sqrt(2.0 / window)', 'sqrt(1.0 / window)', RD),
 ('R17', G, '/ (window - 1.0));', '/ window);', RD),
 ('R18', G, 'float newer = now.light - regionOlderFirst();', 'float newer = now.light;', [RD, 'LightAgeHistoryTests']),
 ('R19', G, '(int(tap) % RegionSide, int(tap) / RegionSide) - RegionRadiusBlocks);', '(int(tap) % RegionSide, int(tap) / RegionSide) - RegionRadiusBlocks + 1);', RD),
 ('R20', G, 'if (index < RegionRingDraws)\n        regionRingBefore', 'if (index < RegionRingDraws - 1u)\n        regionRingBefore', RD),
 ('R21', G, 'slot < (index + 1u) * RegionSlotsPerAdder;', 'slot < (index + 1u) * RegionSlotsPerAdder - 1u;', RD),
 ('R22', G, 'uint first = RegionWindowDraws + (index - RegionAdders) * RegionDrawsPerAdder;', 'uint first = RegionWindowDraws - 1u + (index - RegionAdders) * RegionDrawsPerAdder;', RD),
 ('R23', G, 'min(first + RegionDrawsPerAdder, RegionWindowDraws)', 'first + RegionDrawsPerAdder', RD),
 ('R24', G, 'float before = parameters.reset != 0u ? 0.0 : regionRingBefore[layerBefore()].g;', 'float before = regionRingBefore[layerBefore()].g;', RD),
 ('R25', G, 'moved ? 1.0 : 0.0, 0.0)', '0.0, 0.0)', RD),
 ('R26', G, 'part += vec2(apart, apart * apart);', 'part += vec2(apart, apart * abs(apart) * 0.5);', [RD, 'LightAgeHistoryTests']),
 ('S01', SP, 'if (isRegionAnchor(block))\n        updateRegionAnchor(block);', '', RD),
 ('S02', SP, 'atomicMax(blockEvidence, RegionEvidence);', 'atomicMax(blockEvidence, MotionEvidence);', RD),
 ('S03', SP, 'historyLength = min(historyLength, FullRateFrames);', 'historyLength = min(historyLength, FullRateFrames - 1.0);', RD),
 ('S04', SP, 'const uint RegionEvidence = 1u;', 'const uint RegionEvidence = 4u;', RD),
 ('C01', GT, 'drawIndex % TemporalSignal.RegionRingDraws', 'drawIndex % (TemporalSignal.RegionRingDraws - 1)', RD),
 ('C02', TS, 'foreach (GpuImage region in images.Regions)\n            barriers.Image(region, ImageUse.StorageCompute);', '', RD),
 ('C03', TS, 'return new((blocks.Width + step - 1) / step, (blocks.Height + step - 1) / step);', 'return new(blocks.Width / step, blocks.Height / step);', RD),
 ('C04', TS, '&& AllFit(Regions, AnchorsOf(extent))', '&& AllFit(Regions, extent)', RD),
 ('C05', TS, 'Regions: KeepsLightAge ? [CreateRegionRing(AnchorsOf(extent), part)] : [],', 'Regions: [CreateRegionRing(AnchorsOf(extent), part)],', [AR, 'DescriptorLayoutsTests']),
 ('C06', DL, 'binding is 11 or 13 && HistoryMode.KeepsLightAge', 'binding is 11 && HistoryMode.KeepsLightAge', ['DescriptorLayoutsTests', RD]),
]
out = []
for id, f, orig, rep, cls in m:
    text = open(f'{R}/{f}').read()
    assert text.count(orig) == 1, (id, text.count(orig))
    line = text[:text.index(orig)].count('\n') + 1
    out.append({'id': id, 'file': f, 'line': line, 'original': orig, 'replacement': rep, 'project': P,
                'class': cls})
json.dump(out, open(sys.argv[2], 'w'), indent=1)
print(len(out), 'mutants')
