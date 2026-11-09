---
id: ter-e436
status: in_progress
deps: [tiunr-nuhk, tiunr-i1ho]
links: []
created: 2026-11-08T21:04:29Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-icn3
---
# A rectangle light lets a glowing panel light the scene, with LTC shine

User decision 2026-11-08: after the tube light's follow-ups, add a rectangle light, and compute its specular shine with LTC (linearly transformed cosines, Heitz, Dupuy, Hill and Neubelt 2016), not the tube light's representative point. LTC was made for real-time GPU games with a few polygon lights per pixel and GGX materials, with no noise, so no denoiser. That matches Tertius's exact loop over tens of lights. Shape: a one-sided rectangle (Width, Height) in its pose's local XY plane that shines toward local +Z, a fourth case RectLightData of LightData and RectRenderLight of RenderLight, power in W. Specular: transform the clipped rectangle by the 3x3 matrix from a 64x64 table indexed by roughness and view angle, then the exact polygon edge sum, scaled by a second table for the Fresnel and the magnitude. Fit the tables to our own GGX lobe (GgxLobe.cs), or use the published ones only if their license and their BRDF match ours, and record which. Diffuse: the exact polygon edge sum with no transform, clipped at the horizon. Shadow from the rectangle's center, as the tube light. The glowSent rule holds: a panel that holds the light sends no glow to rays. Rays that hit lit surfaces shade them with the same code. The demo's cabin and bay lamp panels change from point lights to rectangle lights. Check the shine and the diffuse against a numerical integral. Vulkan on Linux: the tables are sampled images in a format with mandatory linear filtering. Later, a separate ticket can move the tube light to LTC's line formula so that both shapes share one method; that is not part of this ticket. Show the user two to four screenshots, smooth and rough floors.


## Notes

**2026-11-08T23:14:16Z**

Design record (implementation, 2026-11-08):
- Table source: our own fit (tools/Tertius.LtcFit, tools/README.md "The LTC table"), not the published tables. Reasons: the published tables fit a GGX with another masking term and parameterization than GgxLobe, so they would not match our lobe; a table of our own also carries no license question. Heitz's method: Nelder-Mead over (m11, m22, m13), cubic error over the sum of densities, 32 x 32 samples of each distribution. The fit takes about 6 s and is deterministic; LtcTableTests checks rows 3, 32 and 63 bit for bit.
- Second table: deviation from the brief. The magnitude and Fresnel come from the existing DFG table (specularWeight), not from a second LTC table. Both are the same integrals of the same lobe, so one store holds them; the LTC table carries only the shape.
- Format: R16G16B16A16_SFLOAT, binding 19 of the frame set, the existing linearClamp sampler. Linear filtering of this format is a mandatory format feature in Vulkan (NVIDIA, RADV, ANV, lavapipe).
- Horizon: clip the polygon (diffuse at the shading normal's horizon; specular at z = 0 of the LTC space, as Heitz). Chosen over the clipped-sphere form factor because a quad clips in one loop over four edges and the clip is exact (diffuse within 0.1 % of the integral).
- Puffs: a lit puff takes the scalar irradiance, face radiance times the solid angle, with no direction. A medium has no surface, so that is the honest answer.
- Fibers: a hair fiber shades the face as a point light along the vector irradiance, dimmed by its length over the solid angle, as for a tube.
- Shadow: from the face's center with radius 0, six atlas tiles. Sharp.
- Accuracy against the numerical integral (white metal, F = 1, units of face radiance): up to 0.6 rad off the normal, within 2.6 % + 0.005; at 1.2 rad, up to +10 % / -13 %, worst 0.048, tail loss up to 0.016. Tolerances in RectLightTests: 3 % + 0.006 and 15 % + 0.02. Why: the fitted cosine cannot follow GGX's long tail and loses up to 10 % below its horizon at grazing views; Heitz's method has this error by construction. Mirror: within 0.02 %. Rough floor energy: 4.6 % short from 40 deg (tolerance 6 %), 22.6 % short from 65 deg (tolerance 25 %). Diffuse and solid angle: within 0.1 % (tolerance 0.1 %).
- Left out: soft area shadows, two-sided panels, textured panels.
- Demo: converted to rectangle lights: the cabin lamps (orbit ship and station), the bay lamps (demo station), the shuttle's cabin lamps, the spin station's panels, the station exit's wall and ceiling lamps. Stay point lights: the lamp halls' bulbs and the spin station's hub lamp (bulbs, not panels). Glowing flat panels that hold no light: the hair-rugs scene's glowing panel (GlowingBox). The marker glows of the orbit cluster, the weightless push and the station's crate are small markers, not panels. New demo scene rect-lights with scripts/rect-lights.json; scripts/rect-lamps.json and scripts/rect-bay.json for the shots.

**2026-11-09T00:01:28Z**

Review (one reviewer subagent, 2026-11-08). The reviewer found no material defect in the LTC math: the fit, normalization, frame, clipping, solid angle and units match Heitz et al. 2016, and the GLSL and C# copies agree. Findings and what was done:
- A side below about 2e-19 m can give the shader 0/0 (the guard comment was false). Fixed, test first: SceneBatcher.MinRectSide = 1 um; two new cases of ARectangleLightWithABrokenFieldDoesNotShine (red before the fix).
- Float cancellation in the edge sum for very bright, small, far faces. Not changed. The suggested cross(a, b - a) does not help in single floats, because b - a carries the rounding of both corners and is not exactly 2w. The real fix passes the side vectors into the sum. The error is about 1e-7 of the face radiance, invisible for every demo panel (2 to 42 W/(m2 sr)). Judgment call.
- LtcTableTests compared bits, which a Linux libm can break. Fixed: the comparison allows 2^-9 of the value plus 1e-4.
- The GPU tests looked only head-on. Fixed: ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight, a rough square leaned 54 deg with the panel off the plane of view, within 3 % (passes).
- No test of the fiber and puff branches. Judgment call: both reduce to existing paths (the fiber to hairResponse along the vector irradiance, as the tube does; the puff to the scalar irradiance that lightIrradiance already returns, which RectLightRenderTests covers). Recorded in the mutation table.
- ARCHITECTURE.md mismatches (side limit 2^62 and the new 1 um floor, eight diffuse placements, roughness 0.045, the mirror's measured vs tested tolerance, the fit frame's z axis, the table layout claim, the terminal's two ceiling lamps, the shadow tile behind the face). All fixed.
- Long theories in the outer class. Fixed: nested classes LtcShineAgainstTheIntegral and RoughFloorEnergy (RectLightTests and LtcTableTests: 16 s to 10.5 s).
- Doc comments over the word limits (RectLightData, RectRenderLight.Rotation, LtcTable). Fixed.
- SceneBatcher's halfLength name and the IsFinite comment. Fixed: EmitterHalfExtent, and the comment now says only what the predicate does.
- The cost is not measured. Measured with the final screenshots (see below).
- The panel glow is a stored copy of the light's face radiance. Not changed: the point-lamp panels already worked so, and a dimmed light is no case in the demo.
Also refactored for the commit hook: Shines split into one method per light case, Prepared's light switch into CaseOf, Nelder-Mead's centroid and shrink into methods (same table bits, md5 bd681a0e), the impostor's spread probes into one switch, the rect-lights scene shortened. Not changed: RenderSnapshotBuilder's long flat switch over the light cases.
Device limit: binding 19 takes the impostor layout's fragment stage to 17 sampled images, one above the spec's least of 16. ARCHITECTURE.md's device row already requires 17; RADV, ANV and lavapipe report far more. DescriptorLayoutsTests updated to 15 and 17.
WalkSliceTests: the roof is no longer darker than the wall, because the ceiling panels send no light up. The roof test became TheCeilingPanelsSendNoLightToTheRoofAroundThem (red on main: 134 beside a panel against 74 away from it; green here: 106 and 106).

**2026-11-09T01:15:29Z**

Hardening (2026-11-08).

Listed mutants (tools/mutate.py, three runs; the rows show each mutant's last run):

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S01 | src/Tertius.Rendering/Shaders/lighting.glsl:699 | rectangle shades as a point light | killed | RectLightRenderTests.ARoughSquareShowsTheCSharpLtcShineOfARectangleLight, RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "facing-the-square"), RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "through-the-square") | 31.2 |
| S02 | src/Tertius.Rendering/Shaders/lighting.glsl:334 | no horizon arc | killed | RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "through-the-square") | 32.8 |
| S03 | src/Tertius.Rendering/Shaders/lighting.glsl:286 | solid angle triple product 2h | survived | Judgment call: on a surface the solid angle multiplies the irradiance and divides the response, so it cancels; it changes only lit puffs and fibers, which no GPU test covers, and C01 locks the C# copy. | 33.4 |
| S04 | src/Tertius.Rendering/Shaders/lighting.glsl:436 | face area halved | killed | RectLightRenderTests.ARoughSquareShowsTheCSharpLtcShineOfARectangleLight, RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "facing-the-square"), RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "through-the-square") | 33.4 |
| S05 | src/Tertius.Rendering/Shaders/lighting.glsl:685 | diffuse without 1/pi | killed | RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "facing-the-square"), RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "through-the-square"), RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "tilted-beside") | 33.9 |
| S06 | src/Tertius.Rendering/Shaders/lighting.glsl:686 | specular without 1/pi | killed | RectLightRenderTests.ARoughSquareShowsTheCSharpLtcShineOfARectangleLight, RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "facing-the-square"), RectLightRenderTests.ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes(name: "tilted-beside") | 34.0 |
| S07 | src/Tertius.Rendering/Shaders/lighting.glsl:679 | LTC drops m02 | killed | RectLightRenderTests.AMirrorReflectsARectangleLightAtItsSize, RectLightRenderTests.ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight | 33.9 |
| S08 | src/Tertius.Rendering/Shaders/lighting.glsl:670 | LTC frame turned 90 deg | killed | RectLightRenderTests.AMirrorReflectsARectangleLightAtItsSize, RectLightRenderTests.ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight | 34.0 |
| S09 | src/Tertius.Rendering/Shaders/lighting.glsl:592 | no geometric horizon test | survived | Judgment call: the geometric horizon matters only where a normal map turns the shading normal away from the geometric one; the point-light path has the same untested guard. | 33.1 |
| S10 | src/Tertius.Rendering/Shaders/lighting.glsl:424 | window from the center's reach | survived | Judgment call: the reach already adds half the diagonal, so measuring the window from the face's reach or from the center's changes the light only in the last per cent of the reach, below the cutoff irradiance. | 33.2 |
| S11 | src/Tertius.Rendering/Shaders/brdf.glsl:117 | LTC view index linear | killed | RectLightRenderTests.AMirrorReflectsARectangleLightAtItsSize, RectLightRenderTests.ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight | 32.7 |
| S12 | src/Tertius.Rendering/Shaders/lighting.glsl:661 | fiber share 1 | survived | Judgment call: the fiber branch scales hairResponse by the vector irradiance's share; no hair card under a rectangle light is in a test, as for the tube. | 33.8 |
| C01 | src/Tertius.Rendering/Shading/RectView.cs:39 | solid angle triple product 2h | killed | RectLightTests.ARectanglesSolidAngleMatchesTheIntegralOverItsFace(name: "across-the-horizon"), RectLightTests.ARectanglesSolidAngleMatchesTheIntegralOverItsFace(name: "mostly-below"), RectLightTests.ARectanglesSolidAngleMatchesTheIntegralOverItsFace(name: "near-overhead") | 26.0 |
| C02 | src/Tertius.Rendering/Shading/RectView.cs:157 | no horizon arc | killed | RectLightTests.ARectangleLightsAPointAsTheIntegralOverItsFaceDoes(name: "mostly-below"), RectLightTests.ARectangleLightsAPointAsTheIntegralOverItsFaceDoes(name: "across-the-horizon") | 4.5 |
| C03 | src/Tertius.Rendering/Shading/RectView.cs:189 | LTC frame turned 90 deg | killed | RectLightTests.AMirrorShowsTheRectangleAtItsSize | 5.1 |
| C04 | src/Tertius.Rendering/Shading/LtcTable.cs:67 | LTC drops m02 | killed | RectLightTests.AMirrorShowsTheRectangleAtItsSize | 5.6 |
| C05 | tools/Tertius.LtcFit/LtcFit.cs:65 | view cosine of a column linear | killed | LtcTableTests.ARowOfTheTableIsTheFitOfOurGgxLobe(row: 32), LtcTableTests.ARowOfTheTableIsTheFitOfOurGgxLobe(row: 63), LtcTableTests.ARowOfTheTableIsTheFitOfOurGgxLobe(row: 3) | 6.3 |
| C06 | src/Tertius.Rendering/Shading/MaterialSample.cs:122 | diffuse without 1/pi | killed | RectLightTests.AMatteSurfaceReflectsItsDiffuseColorOverPiOfTheIrradiance | 5.0 |
| C07 | src/Tertius.Rendering/Shading/MaterialSample.cs:114 | facing away keeps the shine | killed | RectLightTests.AFaceJustBelowTheHorizonSendsNoShineToAGrazingView | 4.8 |
| C08 | src/Tertius.Rendering/Scene/LightUnits.cs:58 | rect intensity P/4pi | killed | BatcherLightTests.ARectangleLightReachesTheShadersAsItsPeakIntensityItsHalfSidesAndAReachThatHoldsItsFace | 7.6 |
| C09 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1227 | reach without half diagonal | killed | BatcherLightTests.ARectangleLightReachesTheShadersAsItsPeakIntensityItsHalfSidesAndAReachThatHoldsItsFace | 4.5 |
| C10 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1242 | full width as half width | killed | BatcherLightTests.ARectangleLightReachesTheShadersAsItsPeakIntensityItsHalfSidesAndAReachThatHoldsItsFace, BatcherLightTests.ARectangleLightWithoutARotationFacesAlongPlusZ | 4.8 |
| C11 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1334 | no floor on the width | killed | BatcherLightTests.ARectangleLightWithABrokenFieldDoesNotShine(broken: "hair-thin-width") | 5.0 |
| C12 | src/Tertius.Rendering/Scene/ImpostorManager.cs:793 | impostor takes light from the back too | killed | ImpostorManagerTests.ARectangleLightThatTurnsItsBackOnTheObjectRecapturesIt | 4.9 |
| C13 | src/Tertius.Rendering/Scene/GpuTypes.cs:358 | half height = half width | killed | BatcherLightTests.ARectangleLightReachesTheShadersAsItsPeakIntensityItsHalfSidesAndAReachThatHoldsItsFace, BatcherLightTests.ARectangleLightWithoutARotationFacesAlongPlusZ, RectLightRenderTests.ARoughSquareShowsTheCSharpLtcShineOfARectangleLight | 7.9 |
| C14 | src/Tertius.Rendering/Scene/PreparedScene.cs:162 | rect main-view test removed | build-failed | Dropped: the arm holds no behavior of the rectangle alone, and any change to it breaks the switch's exhaustiveness. | 2.8 |
| D01 | src/Tertius.Demo/DemoScenes.cs:153 | panel up flipped | killed | LampPanelTests.TheCabinLampsAreRectangleLightsOnTheLowerFacesOfTheirPanels, LampPanelTests.TheBayLampsAreRectangleLightsThatFaceIntoTheBay, ShuttleTests.TheLampsHangOnPillarsClearOfTheGlass | 7.9 |
| D02 | src/Tertius.Demo/DemoScenes.cs:188 | cabin face on the upper side | killed | LampPanelTests.TheCabinLampsAreRectangleLightsOnTheLowerFacesOfTheirPanels | 5.0 |
| D03 | src/Tertius.Demo/DemoScenes.DemoStation.cs:693 | bay face on the wall side | killed | LampPanelTests.TheBayLampsAreRectangleLightsThatFaceIntoTheBay | 5.5 |
| D04 | src/Tertius.Demo/DemoScenes.cs:119 | panel glows twice its light | survived | Judgment call: the scenes build without a renderer in the Host tests, so no test sees a material; RectLightDataTests locks SurfaceRadiance, which the panel takes unchanged. | 5.2 |
| S13 | src/Tertius.Rendering/Shaders/lighting.glsl:670 | LTC first tangent away from the view | killed | RectLightRenderTests.AMirrorReflectsARectangleLightAtItsSize, RectLightRenderTests.ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight | 37.9 |
| S14 | src/Tertius.Rendering/Shaders/lighting.glsl:679 | m02 and m20 swapped | killed | RectLightRenderTests.AMirrorReflectsARectangleLightAtItsSize, RectLightRenderTests.ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight | 37.1 |
32 rows: killed 26, survived 5, build-failed 1

New tests that killed survivors, each red on its mutant first: AMatteSurfaceReflectsItsDiffuseColorOverPiOfTheIrradiance (C06), AFaceJustBelowTheHorizonSendsNoShineToAGrazingView (C07), ARectangleLightThatTurnsItsBackOnTheObjectRecapturesIt (C12). C02, C12 and C13 were rewritten so that they compile (the first forms left a pattern variable unused). The official Stryker run for the Core and Simulation lines is recorded separately.

Coverage of the changed lines (tools/coverage, Debug and Release): 507/519 lines (97.7 %), 101/116 branches (87.1 %). The gaps:
- RectView.CosineVector (no caller): deleted.
- RectView.LtcShare's behind-the-face guard: now tested directly in APointBehindARectangleTakesNoneOfItsLight.
- LtcTable's length check: new test ATableOfAnotherSizeIsRefused.
- SceneBatcher.CaseOf's null arm: unreachable, because Shines refuses the empty light first.
- ShadowTiles' (ConeAim, RectFace) throw: guards a programming error, as the point light's arm does. No test.

Cost: the orbit cabin's view along its five panels took a median frame span of 15.8 ms against 12.1 to 12.7 ms with the bulbs, and the bay view 15.6 ms against 12.3 ms (M1 Max, 1280 x 720, 241 frames, two runs for the cabin). Follow-up: t2-rhja.

Screenshots (240 still draws before each capture, then the capture's own draw: 241 draws in all), in /private/tmp/tertius-rect-light-20261108/final: after-rect-lamps/cabin.png and before-rect-lamps/cabin.png (orbit cabin), after-rect-bay/bay.png and before-rect-bay/bay.png (bay), after-rect-lights/floors.png (mirror and rough floors; main has no such scene). The bright specks in the cabin are on main too (a separate delegate diagnoses them).

**2026-11-09T03:19:45Z**

Official Stryker run (tools/stryker/changed.py main, after merging main, 5,461 s, valid: 0 timeouts, 0 Dubious, load mean 10.6):

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| Simulation-506 | src/Tertius.Simulation/Entities/LightData.cs:99 | Color * ... -> Color / ... | killed | RectLightDataTests.ARectangleSendsItsPowerFromItsFrontFace | - |
| Simulation-507 | src/Tertius.Simulation/Entities/LightData.cs:99 | Power / (pi ...) -> Power * (pi ...) | killed | RectLightDataTests.ARectangleSendsItsPowerFromItsFrontFace | - |
| Simulation-508 | src/Tertius.Simulation/Entities/LightData.cs:99 | Width * Height -> Width / Height | killed | RectLightDataTests.ARectangleSendsItsPowerFromItsFrontFace | - |
| Simulation-509 | src/Tertius.Simulation/Entities/LightData.cs:99 | pi * Width -> pi / Width | killed | RectLightDataTests.ARectangleSendsItsPowerFromItsFrontFace | - |
| Simulation-3761 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:714 | pose.Position - origin -> + origin | survived, then killed | RenderSnapshotLightingTests.ARectangleLightFollowsItsEntityAndTurnsItsFaceWithIt (camera moved to (0, 2, 0)) | 3.7 |
| Simulation-3762 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:715 | previousPose.Position - origin -> + origin | survived, then killed | the same test | 3.9 |
| B01 | RenderSnapshotBuilder.cs:709 (no Stryker mutant) | offset left out | killed | the same test | 6.5 |
| B02 | RenderSnapshotBuilder.cs:710 (no Stryker mutant) | previous pose = current | killed | the same test | 3.8 |
| R01 | src/Tertius.Core/Seams/RenderLight.cs:154 (no Stryker mutant) | generation dropped | survived, then killed | BatcherLightTests.AKeptLightCarriesTheSlotAndTheGenerationOfItsEntity (a rectangle light added) | 6.3 |

The two Stryker survivors were checked again with tools/mutate.py on the same change, not with a second official run of about 1.5 hours: the test had its camera at the origin, so the origin's sign did not show. Line 711 (the constructor call) holds no behavior of its own beyond its arguments. Summary: 9 rows, all killed, 3 of them after a test change.

Suites on the merged branch: Release 10,672 total, 0 failed, 26 skipped.
