---
id: t2-vsqh
status: in_progress
deps: []
links: []
created: 2026-11-12T06:39:26Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, performance, lights]
---
# Key and fill: a corner pass with one fragment per corner and group of lamps, tiles of 16 pixels and no ring walk

Waits for the user's decision on fork 5 of docs/plans/light-pairs.md: whether key and fill goes on. Version 2 of key and fill (branch t2-mlvy-tile-light-budget) keeps the image and saves 0.70 ms of the deck's direct pass and 0.89 ms of the hall's, but its corner and tile pass costs 0.61 ms and 1.04 ms, so it does not win. Cost-only builds (docs/research/light-pairs/measurements.md, variants/density.py and parts.py) show that the pass's time does not follow its count of corners, that the same work in shorter fragments runs in half the time, and that fewer corners and shorter fragments add up: tiles of 16 pixels with 4 x 4 copies of the corner grid, each fragment ranking a sixteenth of the lamps, run in 0.265 ms in the hall and 0.252 ms on the deck without the ring walk. The ring walk (nearestLitPixel in key_fill_corners.frag) costs 0.14 to 0.34 ms in the hall at tiles of 8 pixels and about 1.6 ms at 16. The work: a real corner pass of that shape, with a tile pass that merges the groups' candidates into the same keys as one loop over every lamp gives; a cheap replacement for the ring walk; and the image check of tiles of 16 pixels under motion. The expected gain is about 0.45 ms on the deck and 0.6 ms in the hall, of a direct pass of 4.1 ms.

## Acceptance Criteria

The keys of a corner equal those of version 2's single loop on the test scenes. The deck's and the hall's traces show the corner and tile pass and the direct pass together under every light pair's direct pass by at least 0.4 ms. The stills and a film of the deck, the hall and the cabin stay within version 2's image measures. If the traces do not show the gain, the ticket closes with the measurement and key and fill stays out of main.


## Notes

**2026-11-12T06:59:51Z**

The commit report of cb64ce6e3 names code of the prototype to improve before any merge: VulkanRenderer.cs:139 is 101 lines long (limit 60); ScriptCommand.RendererOverrides.ApplyTo has CC 16; DescriptorLayouts.cs:267-276 deconstructs five or more values; KeyFillImages.cs:122 is a pass-through method; LightPairSetting.cs:29 is unused code; KeyFillPass.cs:68-88 and 94-126 are statement walls. The prototype also has no coverage run, no mutation testing and no ARCHITECTURE.md text. They wait with this ticket, because the prototype does not merge as built.

**2026-11-14T16:08:53Z**

The build of the new corner pass (branch t2-vsqh-key-fill-corner-pass, 2026-11-12 to 2026-11-14). ARCHITECTURE.md, "Key and fill", describes the code, and docs/plans/light-pairs.md, "Version 3: the built corner pass", holds what the build found.

What was built:

- The corner pass draws one fragment per corner and group of lamps, into four images that hold 4 × 4 copies of the corner grid. The tile pass merges the groups' candidates and writes a record of 44 words per tile. The tiles have 16 pixels. No ring walk.
- A pixel counts only the corners on its own surface: near its plane, and with a normal within about 26 degrees. Under a summed weight of 1/16, it takes an open share of every lamp's full response. This repaired a fault that the first stills showed: thin bright edges lost their highlight, by up to 80 steps of 255 on the deck and 169 in the hall.
- The direct shader stays at register 115 on Apple's GPU. At register 116 the GPU keeps 384 threads resident and not 448, and the pass loses its gain. The merge of main on 2026-11-14 showed how near the step is: main's specular target of 8-bit counts alone moved the shader to register 116, and the deck's two passes took 3.85 ms against 3.82 ms for every light pair. A new form of the loop returned it to 115: the fill goes into the sum before the full response, and the loop holds one weight across pathLight. Ticket t2-x53j holds the guard that is missing.
- `RendererOptions.LightPairs` is `KeyAndFill.Default`, 4 key lights. The test helpers keep every light pair, so that the reference images hold the exact light. The host has `--key-lights off|N` and `--exposure STOPS`.

The measurements on the merged code, 4 key lights against every light pair (2026-11-14):

| Scene | Machine | Direct pass, every light pair | Corner and tile pass | Direct pass | Both |
|---|---|---:|---:|---:|---:|
| Deck | Apple M1 Max, take 8, two valid traces each | 3.82 ms | 0.25 ms | 3.22 ms | −0.35 ms |
| Hall | Apple M1 Max, take 8, two to four valid traces each | 3.83 ms | 0.27 ms | 3.21 ms | −0.35 ms |
| Deck | RTX 2000 Ada, Linux, 5 runs in one job | 1.62 ms | 0.10 ms | 1.55 ms | +0.02 ms |
| Hall | RTX 2000 Ada, Linux, 5 runs in one job | 1.75 ms | 0.11 ms | 1.30 ms | −0.34 ms |

The acceptance criterion asked for 0.4 ms of the two passes on the Mac. The build reaches 0.35 ms on the deck and in the hall. Without the repair of the thin edges it reached 0.50 ms on 2026-11-12. Ticket t2-lcmo holds the 0.19 ms that the repair costs. On the Mac, the deck's median frame span fell from 20.02 ms to 19.71 ms with 3 key lights.

On the card, the hall's frame span fell by 0.33 ms of 6.04 ms. The deck is neutral there: +0.015 ms of 8.37 ms, with runs that overlap. The job of 2026-11-12 gave the card's deck a gain of 0.12 ms. Since then the deck's direct pass with every light pair fell from 1.74 ms to 1.62 ms on the card, and the pass under key and fill did not fall with it. The cause is not known.

The key-light count is 4, by the orchestrator's rule: 4 if a valid hall take shows that the fourth costs no more than the run-to-run spread. Take 8 decided it. With a GPU clock above 1,200 MHz, the hall's two passes took 3.479, 3.489 and 3.472 ms with 3 key lights, and 3.467 and 3.492 ms with 4. On the card, the frame spans with 3 and with 4 key lights lie within 0.01 ms of each other in both scenes.

The image with 4 key lights, stills at 1920 × 1080 with one fixed exposure per scene, on the merged code:

| Scene | Level | Blotch | Pixels changed by more than 2 steps of 255 | Greatest change | FLIP, mean |
|---|---:|---:|---:|---:|---:|
| Deck | −0.31 % | 0.36 % | 0.14 % | 12 steps | 0.012 |
| Hall, 64 lamps | −2.47 % | 2.29 % | 5.26 % | 53 steps | 0.018 |
| Cabin | −0.08 % | 0.33 % | 0.14 % | 59 steps | 0.009 |

The deck and the cabin stay within version 2's measures, and give the same numbers with 3 key lights. The hall's floor is darker: a crop of 480 × 320 pixels of the floor has a mean of 111.03 against 112.74 of 255, and a mean FLIP of 0.049 (0.051 with 3 key lights). The cause is the missing specular light of the fill lamps: a variant with every lamp's specular light in full equals every light pair within 1 step of 255 (a still on the RTX 2000 Ada). Ticket t2-kibe holds a cheap specular fill, which is not built. The motion check on the hall's film: the ratio to every light pair changes by 2.44 % between two frames at the 99th percentile, against 3.70 % for version 2.

The key and fill tests also ran on the RTX 2000 Ada under NVIDIA's driver, and all passed.

Two forms that did not gain on the Mac: a search of the tile's list after the shadow lookup (0.06 ms more on the deck, 0.29 ms more in the hall), and the corners' normals as three 8-bit counts (no change).

The mutants, with `tools/mutate.py`. The line numbers are those of the commit d499c510d, and for S26 to S28 and S32 to S34 those of the new form of the loop. The branch changes no line of Core, Simulation or Physics, so there is no Stryker run.

| id | place | change | result | killed by |
|---|---|---|---|---|
| C01 | Rendering/LightPairSetting.cs:63 | key share starts at 0 loss, not at the threshold | killed | KeyAndFillTests.AtMostTheKeyLightsOfACornerHaveAKeyShare(keyLights: 4) |
| C02 | Rendering/LightPairSetting.cs:63 | no threshold gives no key | killed | KeyAndFillTests.WithoutAThresholdALightThatLosesSomethingIsAllKey |
| C03 | Rendering/LightPairSetting.cs:78 | no floor of negligible loss | killed | KeyAndFillTests.ANegligibleLossHasNoKeyShareAlsoAtACornerWithMoreLightsThanKeyLights |
| C04 | Rendering/LightPairSetting.cs:78 | threshold from the last key light | killed | KeyAndFillTests.ALightBetweenOneAndTwoThresholdsSharesItsLightBetweenFullAndFill |
| C05 | Rendering/LightPairSetting.cs:96 | five key lights pass the check | killed | KeyAndFillTests.AKeyLightCountOutsideOneToFourHasAProblem(keyLights: 5) |
| C06 | Rendering/LightPairSetting.cs:96 | zero key lights pass the check | killed | KeyAndFillTests.AKeyLightCountOutsideOneToFourHasAProblem(keyLights: 0) |
| C07 | Rendering/Frame/KeyFillImages.cs:74 | tiles along x rounded down | killed | KeyAndFillRenderTests.AtACornerOfTheTileGridEachLampShinesByTheKeyShareOfItsLoss |
| C08 | Rendering/Frame/KeyFillImages.cs:75 | tiles along y rounded down | killed | KeyAndFillRenderTests.TheKeyLightsAreTheLampsOfTheGreatestLossWhereverTheyStandInTheLightList(first: 14, second: 15) |
| C09 | Rendering/Frame/KeyFillImages.cs:85 | one corner column short | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| C10 | Rendering/Frame/KeyFillImages.cs:85 | one corner row short | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| C11 | Rendering/Passes/KeyFillPass.cs:130 | the tile pass always takes four key lights | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| C12 | Rendering/Passes/KeyFillPass.cs:129 | the last tile row is not written | killed | KeyAndFillRenderTests.TheKeyLightsAreTheLampsOfTheGreatestLossWhereverTheyStandInTheLightList(first: 14, second: 15) |
| C13 | Rendering/Passes/KeyFillPass.cs:142 | the dispatch misses the last columns | killed | KeyAndFillRenderTests.AtACornerOfTheTileGridEachLampShinesByTheKeyShareOfItsLoss |
| C14 | Rendering/Passes/KeyFillPass.cs:82 | the corner pass draws with the render viewport | survived |  |
| C15 | Rendering/Passes/DirectLightPass.cs:34 | the direct pass never uses key and fill | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| C16 | Rendering/VulkanRenderer.cs:450 | the renderer does not check the setting | killed | KeyAndFillTests.AKeyLightCountOutsideOneToFourIsRefusedWhenTheRendererIsMade(keyLights: 5) |
| C17 | Rendering/VulkanRenderer.cs:1038 | no resize of the key and fill images | killed | KeyAndFillRenderTests.AfterAResizeKeyAndFillRendersAsARendererMadeAtTheNewSize |
| C18 | Rendering/Passes/KeyFillPass.cs:125 | the tile pass counts the tiles of the largest render size | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| C19 | Rendering/Frame/KeyFillImages.cs:23 | the tile records have half the bytes | survived |  |
| H01 | Host/ScriptCommand.Options.cs:129 | --key-lights N gives one key light | killed | ScriptCommandOptionsTests.TheKeyLightsOptionChoosesKeyAndFillWithThatManyKeyLights(count: "4") |
| H02 | Host/ScriptCommand.Options.cs:127 | --exposure fixes the exposure one stop higher | killed | ScriptCommandOptionsTests.TheExposureOptionFixesTheExposureAtItsStops |
| H03 | Host/ScriptCommand.cs:1028 | 65 stops pass | killed | ScriptCommandTests.AnExposureOptionThatIsNotANumberOfStopsFromMinusSixtyFourToSixtyFourIsAProblem(value: "-64.5") |
| H04 | Host/ScriptCommand.cs:1033 | 0 and 5 key lights pass | killed | ScriptCommandTests.AKeyLightsOptionThatIsNeitherOffNorAWholeNumberFromOneToFourIsAProblem(value: "0") |
| H05 | Host/ScriptCommand.cs:141 | no check of the value of --key-lights | killed | ScriptCommandTests.AKeyLightsOptionThatIsNeitherOffNorAWholeNumberFromOneToFourIsAProblem(value: "2.5") |
| S01 | Rendering/Shaders/key_fill.glsl:77 | key share starts at 0 loss | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S02 | Rendering/Shaders/key_fill.glsl:52 | a corner of the far side samples outside the image | survived |  |
| S03 | Rendering/Shaders/key_fill_corners.frag:87 | the groups of the lower copies rank the first rows lights | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S04 | Rendering/Shaders/key_fill_corners.frag:101 | the loss leaves out the shine | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S05 | Rendering/Shaders/key_fill_corners.frag:101 | the loss ignores the shadow | survived |  |
| S06 | Rendering/Shaders/key_fill_corners.frag:80 | the corner keeps the narrow lobe | killed | KeyAndFillRenderTests.ASceneWithNoMoreLampsThanKeyLightsKeepsTheShineOfItsHighlights(roughness: 0.150000006) |
| S07 | Rendering/Shaders/key_fill_corners.frag:71 | the corner writes the opposite normal | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S08 | Rendering/Shaders/key_fill_corners.frag:100 | no whole light, so no negligible floor | killed | KeyAndFillRenderTests.AtACornerOfTheTileGridEachLampShinesByTheKeyShareOfItsLoss |
| S09 | Rendering/Shaders/key_fill_corners.frag:102 | every key says point light | killed | KeyAndFillRenderTests.KeyLightsOfEveryShapeKeepTheirShine |
| S10 | Rendering/Shaders/key_fill_corners.frag:73 | a fiber corner ranks lights | survived |  |
| S11 | Rendering/Shaders/key_fill_tiles.comp:39 | ties go to the later light | survived |  |
| S12 | Rendering/Shaders/key_fill_tiles.comp:91 | no floor of negligible loss | killed | KeyAndFillRenderTests.AtACornerOfTheTileGridEachLampShinesByTheKeyShareOfItsLoss |
| S13 | Rendering/Shaders/key_fill_tiles.comp:90 | the threshold is always the fifth loss | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S14 | Rendering/Shaders/key_fill_tiles.comp:95 | the second slot is key with one key light | survived |  |
| S15 | Rendering/Shaders/key_fill_tiles.comp:69 | the merge reads only the first row of copies | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S16 | Rendering/Shaders/key_fill_tiles.comp:136 | every share goes to the first corner | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S17 | Rendering/Shaders/key_fill_tiles.comp:118 | no corner normal in the record | killed | KeyAndFillRenderTests.OnAWallThatLeansEachCornerStillShinesByTheKeyShareOfItsLoss |
| S18 | Rendering/Shaders/key_fill_light.glsl:23 | a rectangle fills as a point of full strength | killed | KeyAndFillRenderTests.ATubeOrAPanelThatIsNotKeyKeepsItsDiffuseLight |
| S19 | Rendering/Shaders/key_fill_light.glsl:64 | no test of the corner normal | killed | KeyAndFillRenderTests.ASurfaceThatFacesAnotherWayThanTheCornersAroundItTakesEveryLampInFull |
| S20 | Rendering/Shaders/key_fill_light.glsl:64 | no test of the corner plane | killed | KeyAndFillRenderTests.ASurfaceThatIsNotTheOneOfTheCornersAroundItTakesEveryLampInFull(leanDegrees: 0, inFront: 0.600000024) |
| S21 | Rendering/Shaders/key_fill_light.glsl:69 | no open share | killed | KeyAndFillRenderTests.ASurfaceThatNoCornerLiesOnTakesEveryLampInFull |
| S22 | Rendering/Shaders/key_fill_light.glsl:64 | an empty corner counts | survived |  |
| S23 | Rendering/Shaders/key_fill_light.glsl:83 | the fourth corner has no say | killed | KeyAndFillRenderTests.ASceneWithNoMoreLampsThanKeyLightsKeepsTheShineOfItsHighlights |
| S24 | Rendering/Shaders/key_fill_light.glsl:109 | the cursor never moves | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S25 | Rendering/Shaders/key_fill_light.glsl:119 | a light that is not in the list has no open share | killed | KeyAndFillRenderTests.ASurfaceThatNoCornerLiesOnTakesEveryLampInFull |
| S26 | Rendering/Shaders/key_fill_light.glsl:125 | the fill is not reduced by the key share | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S27 | Rendering/Shaders/key_fill_light.glsl:128 | a key light shines through its shadow | killed | KeyAndFillRenderTests.AKeyLampsShineStaysBehindItsShadow |
| S28 | Rendering/Shaders/key_fill_light.glsl:125 | no shadow on the fill | killed | KeyAndFillRenderTests.ALampThatIsNotKeyKeepsItsDiffuseLightAndItsShadow(keyLights: 2) |
| S29 | Rendering/Shaders/key_fill_light.glsl:119 | a fiber takes key shares | killed | KeyAndFillRenderTests.AHairSurfaceTakesEveryLampInFull |
| S30 | Rendering/Shaders/key_fill_light.glsl:68 | every corner takes the first corner weight | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S31 | Rendering/Shaders/key_fill_tiles.comp:151 | the tile list in the order of the light list alone, not of the shapes | killed | KeyAndFillRenderTests.KeyLightsOfEveryShapeKeepTheirShine |
| S32 | Rendering/Shaders/key_fill_light.glsl:128 | a key light's full response is not scaled by its key share | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |
| S33 | Rendering/Shaders/key_fill_light.glsl:124 | a fiber takes the fill too | survived |  |
| S34 | Rendering/Shaders/key_fill_light.glsl:130 | a key light loses the diffuse part of its full response | killed | KeyAndFillRenderTests.ARenderSizeBelowTheLargestKeepsEachCornersKeyShares |

58 mutants: killed 49, survived 9. The rows S26, S27 and S28 ran again on the new form of the loop, with S32, S33 and S34, and the table shows that run for them.

The answers for the 9 survivors:

- C14, equivalent: the render size is always larger than the copies of the corner grid, so the pass draws every texel that the tile pass reads under either viewport. The viewport only saves the texels that no tile reads.
- C19, judgment call: no test reaches a write past the end of a storage buffer without GPU-assisted validation. The size is one product beside the record's layout in key_fill.glsl.
- S02, judgment call: the clamp keeps a corner's fetches inside the image, as the Vulkan spec asks. On this GPU a fetch outside gives 0, which both passes read as an empty corner, so no pixel test can tell.
- S05, judgment call: a corner in a lamp's full shadow skips the lamp before this line. The factor only scales the loss of a corner inside a penumbra, and no test pins one corner there.
- S10, judgment call: the early return saves the corner's lamp loop. A fiber's pixels take every lamp in full whatever their corner holds, and another surface's pixels reject the fiber's corner by the surface test.
- S11, equivalent: a lamp that ties with the lamp after the key lamps has a loss equal to the threshold, so its key share is 0 whichever comes first. Lamps that tie above the threshold are all key lamps.
- S14, equivalent: the threshold is at least the loss of the slot after the key lamps, so every slot past them has a key share of 0 without the flag. The flag stays as the plain statement of the rule.
- S33, equivalent: a fiber's key share is 1, so its fill has the weight 0 with or without the test. The test only lets the compiler drop the fill's code from the fiber's loops.
- S22, judgment call: an empty corner's record gives the eye's own place, which the plane test rejects for every surface that does not pass within a tile's size of the eye. No test scene has such a surface.

The coverage of the changed lines (Debug, Rendering and Host): 293 of 297 lines and 66 of 68 branches. The 4 lines are the cleanup in KeyFillPass after the second pipeline fails to compile, which no test can cause. The 2 branches are the arm of a switch over the setting that no value reaches.

The commit report named four findings:

- The constructor of VulkanRenderer has 77 lines (limit 60). It had 76 lines before this ticket, which adds one line. It is a flat list of one creation per owned part in the order of their needs, and the fields are read-only, so the creations cannot move into a method that assigns them.
- `ScriptCommand.RendererOverrides.ApplyTo` has a cyclomatic complexity of 16. It is a flat list of one test per override, which is its clearest form, and this ticket adds two overrides.
- A test helper of 68 lines is now three methods: the window's renderer, the renders that bring the render scale to a half, and the read.
- The key fill tiles layout no longer adds an eighth value to the tuple of compute layouts. The constructor of DescriptorLayouts creates it on its own line.

The suites on the merged code with 4 key lights (2026-11-14, the Mac): Checked 10,911 passed and 0 failed, Release 10,898 passed and 0 failed, 26 skipped in each. The key and fill tests passed on the RTX 2000 Ada on 2026-11-12, before the merge and the new form of the loop.
