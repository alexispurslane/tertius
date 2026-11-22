# Tool code of t0-acxm. mutants.py <repository> <out.json> [second]: writes the tools/mutate.py list of the remedy
# (mutants/list.json, from the tree of 5085e1f1b), with each mutant's line found from its original text. With `second`,
# it writes the list of the lines that changed after the settlement of 2026-11-22 (mutants/list-2.json): the bounce
# light alone, the restart at a render size change, and the anti-lag under region evidence; M12 and M13 after the review.
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
CS = 'src/Tertius.Rendering/Tertius.Rendering.csproj'
second = [
 ('M01', G, 'return parameters.reset == 0u && all(equal(frame.previousViewport.xy, frame.viewport.xy));',
  'return parameters.reset == 0u;', RD),
 ('M02', G, 'return parameters.reset == 0u && all(equal(frame.previousViewport.xy, frame.viewport.xy));',
  'return all(equal(frame.previousViewport.xy, frame.viewport.xy));', RD),
 ('M03', G, 'ivec2 blocks = (ivec2(frame.viewport.xy) + 7) / 8;', 'ivec2 blocks = ivec2(frame.viewport.xy) / 8;', RD),
 ('M04', G, 'return (blocks - 1) / AnchorStepBlocks;', 'return blocks / AnchorStepBlocks;', RD),
 ('M05', G, 'AnchorStepBlocks, lastRegionAnchor());', 'AnchorStepBlocks, imageSize(regionRing).xy - 1);', RD),
 ('M06', G, 'float before = regionContinues() ? regionRingBefore[layerBefore()].g : 0.0;',
  'float before = regionRingBefore[layerBefore()].g;', RD),
 ('M07', SP, 'historyLength = min(antiLagged, FullRateFrames);', 'historyLength = min(historyLength, FullRateFrames);', RD),
 ('M08', SP, 'historyLength = min(antiLagged, FullRateFrames);', 'historyLength = antiLagged;', RD),
 ('M09', CS, ' -DTERTIUS_LIGHT_AGE -DTERTIUS_REGION_DRIFT -I', ' -DTERTIUS_LIGHT_AGE -I', [RD, 'ShaderVariantTests']),
 ('M10', TS, 'public bool TestsRegionDrift => KeepsLightAge && Parameters.Light == SignalLight.Diffuse &&',
  'public bool TestsRegionDrift => KeepsLightAge &&', RD),
 ('M11', TS, 'public bool TestsRegionDrift => KeepsLightAge && ', 'public bool TestsRegionDrift => ',
  [AR, 'DescriptorLayoutsTests', '*+EvidenceUnderEachHistory']),
 ('M12', TS, '&& Parameters.Light == SignalLight.Diffuse && !singleDiffuseHistory;',
  '&& Parameters.Light == SignalLight.Diffuse;', '*+NeighborLimiterPrototype'),
 ('M13', 'src/Tertius.Rendering/Frame/FrameHistory.cs', 'PreviousRenderSize: drawn?.RenderSize ?? renderSize',
  'PreviousRenderSize: drawn is { } last && last.Camera.FrameId == snapshot.OriginFrameId ? last.RenderSize : renderSize',
  'FrameHistoryTests'),
]
if sys.argv[3:] == ['second']:
    m = second
out = []
for id, f, orig, rep, cls in m:
    text = open(f'{R}/{f}').read()
    assert text.count(orig) == 1, (id, text.count(orig))
    line = text[:text.index(orig)].count('\n') + 1
    out.append({'id': id, 'file': f, 'line': line, 'original': orig, 'replacement': rep, 'project': P,
                'class': cls})
json.dump(out, open(sys.argv[2], 'w'), indent=1)
print(len(out), 'mutants')
