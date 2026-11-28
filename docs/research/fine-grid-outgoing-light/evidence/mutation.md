# Mutation run of the change (first pass, 2026-11-28)

Command: `nix develop --command python3 -I tools/mutate.py docs/research/fine-grid-outgoing-light/tools/mutants.json <out>` on `eff66e481`, Checked.

| id | place | change | result | killed by | s |
| C01 | src/Tertius.Rendering/Passes/LightVolumePass.cs:153 | Skip direct shading | killed | LightVolumeInheritanceTests.AnInheritedEmitterLightsACoarseReceiverWithoutDirectLights, LightVolumeInheritanceTests.AShadowedNeighbourInTheSameCoarseTileDoesNotDarkenASunlitReflectorForACoarseReceiver, LightVolumeInheritanceTests.AReflectorThatCrossesTheFineGridsFaceAsTheCameraMovesKeepsItsLightOnACoarseReceiver | 15.1 |
| S01 | src/Tertius.Rendering/Shaders/volume_shade.frag:74 | Drop local reflected light | killed | LightVolumeInheritanceTests.AShadowedNeighbourInTheSameCoarseTileDoesNotDarkenASunlitReflectorForACoarseReceiver, LightVolumeInheritanceTests.AReflectorThatCrossesTheFineGridsFaceAsTheCameraMovesKeepsItsLightOnACoarseReceiver, LightVolumeInheritanceTests.AnOpaqueSheetInsideTheFineGridStillBlocksAReflectorFromACoarseReceiver | 14.2 |
| S02 | src/Tertius.Rendering/Shaders/volume_shade.frag:74 | Drop local emission | killed | LightVolumeInheritanceTests.AnInheritedEmitterLightsACoarseReceiverWithoutDirectLights, LightVolumeInheritanceTests.AReflectorLitOnlyByBouncedLightStillTintsACoarseReceiver | 16.1 |
| S03 | src/Tertius.Rendering/Shaders/volume_shade.frag:74 | Omit the diffuse source's area weight | killed | LightVolumeInheritanceTests.AShadowedNeighbourInTheSameCoarseTileDoesNotDarkenASunlitReflectorForACoarseReceiver, LightVolumeInheritanceTests.AReflectorThatCrossesTheFineGridsFaceAsTheCameraMovesKeepsItsLightOnACoarseReceiver, LightVolumeInheritanceTests.AnOpaqueSheetInsideTheFineGridStillBlocksAReflectorFromACoarseReceiver | 16.9 |
| S04 | src/Tertius.Rendering/Shaders/volume_shade.frag:62 | Sample direct light in the wrong grid | killed | LightVolumeInheritanceTests.AReflectorThatCrossesTheFineGridsFaceAsTheCameraMovesKeepsItsLightOnACoarseReceiver, LightVolumeInheritanceTests.AnOpaqueSheetInsideTheFineGridStillBlocksAReflectorFromACoarseReceiver | 15.9 |
| S05 | src/Tertius.Rendering/Shaders/volume_restrict.comp:34 | Drop inherited outgoing light | killed | LightVolumeInheritanceTests.AnInheritedEmitterLightsACoarseReceiverWithoutDirectLights, LightVolumeInheritanceTests.AShadowedNeighbourInTheSameCoarseTileDoesNotDarkenASunlitReflectorForACoarseReceiver, LightVolumeInheritanceTests.AReflectorThatCrossesTheFineGridsFaceAsTheCameraMovesKeepsItsLightOnACoarseReceiver | 15.3 |
| S06 | src/Tertius.Rendering/Shaders/volume_restrict.comp:40 | Keep inherited source in child face-area units | killed | LightVolumeInheritanceTests.AReflectorThatCrossesTheFineGridsFaceAsTheCameraMovesKeepsItsLightOnACoarseReceiver | 13.8 |
| S07 | src/Tertius.Rendering/Shaders/volume_restrict.comp:40 | Overwrite the parent shell's outgoing light | killed | LightVolumeInheritanceTests.AReflectorThatCrossesTheFineGridsFaceAsTheCameraMovesKeepsItsLightOnACoarseReceiver | 13.8 |
| S08 | src/Tertius.Rendering/Shaders/volume_source.comp:37 | Treat area-weighted light as radiance | survived |  | 14.0 |
| S09 | src/Tertius.Rendering/Shaders/volume_source.comp:39 | Remove the second bounce | killed | LightVolumeInheritanceTests.AReflectorLitOnlyByBouncedLightStillTintsACoarseReceiver | 14.2 |
| S10 | src/Tertius.Rendering/Shaders/volume_surface.frag:58 | Omit emission's area weight | survived |  | 14.2 |
| S11 | src/Tertius.Rendering/Shaders/volume_restrict.comp:38 | Drop inherited occlusion | killed | LightVolumeInheritanceTests.AnInheritedEmitterLightsACoarseReceiverWithoutDirectLights, LightVolumeInheritanceTests.AShadowedNeighbourInTheSameCoarseTileDoesNotDarkenASunlitReflectorForACoarseReceiver, LightVolumeInheritanceTests.AnOpaqueSheetInsideTheFineGridStillBlocksAReflectorFromACoarseReceiver | 18.3 |
| S12 | src/Tertius.Rendering/Shaders/volume_shade.frag:70 | Skip sources on the outer guard's last Z face | killed | LightVolumeRenderTests.ASunlitReflectorInTheOuterGridsShellLightsAReceiverNearTheCamera | 33.8 |
| S13 | src/Tertius.Rendering/Shaders/volume_shade.frag:70 | Divide by the area of empty buckets | killed | LightVolumeInheritanceTests.AnInheritedEmitterLightsACoarseReceiverWithoutDirectLights, LightVolumeInheritanceTests.AShadowedNeighbourInTheSameCoarseTileDoesNotDarkenASunlitReflectorForACoarseReceiver, LightVolumeInheritanceTests.AReflectorThatCrossesTheFineGridsFaceAsTheCameraMovesKeepsItsLightOnACoarseReceiver | 15.6 |
| S14 | src/Tertius.Rendering/Shaders/volume_shade.frag:73 | No restraint on direct diffuse light | killed | LightVolumeRenderTests.ANeutralOffscreenReflectorAddsFarLessIndirectLightThanAColoredOneOfTheSamePeakAlbedo | 52.9 |
| S15 | src/Tertius.Rendering/Shaders/volume_shade.frag:72 | Leave emission in fixed-point units | survived |  | 54.9 |
| S16 | src/Tertius.Rendering/Shaders/volume_shade.frag:66 | Shade guard-shell cells outside the grid | killed | LightVolumeRenderTests.AWallAtAFixedDistanceInTheOuterFadeGetsTheSameLightWhereverTheGridSnaps(distance: 6.80000019), LightVolumeRenderTests.AWallAtAFixedDistanceInTheOuterFadeGetsTheSameLightWhereverTheGridSnaps(distance: 7.19999981) | 40.4 |
| S17 | src/Tertius.Rendering/Shaders/volume_shade.frag:16 | Evaluate lights when the list is empty | killed | LightVolumeRenderTests.AnOffscreenEmitterLightsTheReceiverAndOpaqueSheetsReduceItByProjectedArea | 42.5 |
| S18 | src/Tertius.Rendering/Shaders/volume_shade.frag:51 | Clamp direct light from below, not above | killed | LightVolumeInheritanceTests.AnOpaqueSheetInsideTheFineGridStillBlocksAReflectorFromACoarseReceiver | 41.2 |
| C02 | src/Tertius.Rendering/Passes/LightVolumePass.cs:23 | Buffer 16 bytes per cell too small | killed | LightVolumeLayoutTests.OpenSkyReachesEveryTransportDirectionAndFilteredLobeIncludingTheLastDispatchCells | 55.2 |
20 rows: killed 17, timeout 0, survived 3, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Coverage: `dotnet test --project tests/Tertius.Rendering.Tests -c Checked --filter-class "*LightVolume*" --coverage` ran 35 tests, 0 failed. `tools/coverage/changed-lines.py --base d3123fe71` reports the changed executable C# lines covered: 1 of 1 (`LightVolumePass.cs`, the moved `ShadeGroups(context)` call). The other changed C# lines are constant declarations. No coverage tool reads the shaders, so the mutants above cover the shader lines.

## Survivors

S08, S10 and S15 change the absolute scale of the light that a coarse receiver gets: S08 multiplies each source by its area in face units, S10 drops the area weight of emission in the raster, and S15 leaves emission in the raster's fixed-point units, a factor of 4. Every earlier test compares two scenes rendered with the same code, so a uniform scale passes them. The new test `AnInheritedEmitterLightsACoarseReceiverWithTheRadianceThatItsSizeAndDistanceGive` expects an inherited emitter to light the coarse receiver within a factor of two of L A cos θ cos θ' / (d² π). It passes on the fix and fails on each of the three mutants:

| id | place | change | result | killed by | s |
| S08 | src/Tertius.Rendering/Shaders/volume_source.comp:37 | Treat area-weighted light as radiance | killed | LightVolumeInheritanceTests.AnInheritedEmitterLightsACoarseReceiverWithTheRadianceThatItsSizeAndDistanceGive | 14.3 |
| S10 | src/Tertius.Rendering/Shaders/volume_surface.frag:58 | Omit emission's area weight | killed | LightVolumeInheritanceTests.AnInheritedEmitterLightsACoarseReceiverWithTheRadianceThatItsSizeAndDistanceGive | 14.8 |
| S15 | src/Tertius.Rendering/Shaders/volume_shade.frag:72 | Leave emission in fixed-point units | killed | LightVolumeInheritanceTests.AnInheritedEmitterLightsACoarseReceiverWithTheRadianceThatItsSizeAndDistanceGive | 40.0 |
3 rows: killed 3, timeout 0, survived 0, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Final result: 20 mutants, 20 killed, 0 survived.
