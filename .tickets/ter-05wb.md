---
id: ter-05wb
status: closed
deps: []
links: []
created: 2026-11-09T20:22:30Z
type: feature
priority: 0
assignee: Alexis Purslane
parent: ter-8hyd
---
# Aim part of the diffuse rays at bright lit pixels on screen, so rare sunlit patches stop making blotches

Diagnosis 2026-11-09, /private/tmp/tertius-motion-blotches-20261109/: in the bay's moving sunbeam and the cabin walk, 0.13 to 2.1 % of diffuse samples (those at least 8 times the settled light) carry 36 to 86 % of the bounce light. They are rays that happen to hit small sunlit patches about 100 times as bright as the walls. At 0.27 to 0.35 rays per pixel per frame, no history or filter averages them out, and every cheap knob either loses light or gains under 12 %. Decision (parent, under the user's goal of 2026-11-09, which rules out ReSTIR): importance-sample the diffuse rays toward bright lit pixels on screen. Each 2x2 block's diffuse ray is drawn either from the cosine lobe or toward a screen pixel picked in proportion to its directly lit, albedo-weighted outgoing light, from a luminance sum pyramid of this frame (or the reprojected last frame if the order of passes needs it). Each ray is weighted by one-sample multiple importance sampling against both densities, with the pixel-to-solid-angle Jacobian, so the estimate stays unbiased. The existing screen walk gives visibility. No sample is kept or shared between pixels or frames (not ReSTIR). Situation check by the study: Lumen guides screen-probe rays by last frame's light at console 1080p; Reflective Shadow Maps sample the pixels that a light lights; our bright patches are on screen and the direct-light image holds them. Expected cost 0.3 to 0.5 ms with split rays. Loses: bright surfaces off screen are not guided (as today). Acceptance: the study's blotch score (4 px Gaussian, against a long still truth) on the bay frames 300 and 360 and the walk frames 86 and 110 falls far, toward the survey's clean threshold; the indirect light of long still runs stays within 3 % of the cosine-only estimate (no bias); the speck counts of still scenes do not rise; the lamp hall and other scenes show no new artifacts; the GPU cost is measured.


## Notes

**2026-11-10T16:35:55Z**

Prototype, 2026-11-09 (one agent, branch ter-05wb-guided-diffuse-rays):

Built 2026-11-09 on branch ter-05wb-guided-diffuse-rays. Logs, scripts and scores: /private/tmp/tertius-guided-rays-20261109/ (run2.sh, batch.sh, all2.sh, score2.py copied from the study, local.py, stillmean.py, specks.py, timing2.sh).

Model and pass order: see ARCHITECTURE.md, "Guided diffuse rays". The guide light is this frame's direct light, because the direct pass runs before the tracer.

Target boxes tried, by the CPU variance of a floor's and a wall's bounce light against cosine only (GuidedRaysTests): the slab's middle plane gave a higher variance than cosine; the front face gave 2x cosine for a floor patch seen from a wall (rays from behind enter through the side); the whole slab was good on the CPU, but on the GPU about 85 % of the guided rays met a neighbor's slab first. A box from the center depth to kappa/2 behind it, with the 3 x 3 counting rule, gave variance ratios of 0.003 to 0.031 (box share 0.1: 0.007 to 0.046; 0.025: up to 0.66; 0.01: up to 1.2).

Blotch score of the study (score2.py, against long stills with the clamp off), main / g = 1/2 / g = 1/4: bay 300: 2.3 / 0.1 / 0.1 %; bay 360: 0.8 / 2.2 / 1.6 %; walk 86: 15.5 / 12.6 / 13.8 %; walk 110: 19.3 / 13.1 / 14.0 %. Small-scale part above 10 % (E minus its 24 px Gaussian): bay 300: 1.5 / 0.4 / 0.5 %; bay 360: 1.0 / 0.5 / 0.5 %; walk 86: 11.0 / 8.3 / 8.1 %; walk 110: 13.7 / 12.2 / 11.1 %. The acceptance "falls far" holds for the small-scale blotches of the bay, not for the study's score: the rest of its area is large regions that lag the moving light (view/bay360.png, view/walk86.png). g stays 1/2: the two shares differ by less than the frames differ.

TERTIUS_GUIDED_RAYS=off gives main's scores exactly on the bay (bay-off against bay-main).

Long stills (1024 warm-up draws, clamp off, cap 128), g = 1/2 against cosine-only main: bay 150/240/300/360: -0.03, +0.08, -0.07, +0.11 %; walk 40/86/110: -0.79, -0.22, -0.31 %; lamp hall: -0.15 %.

Specks per 10,000 lit pixels, draws 120 to 159 of a still at the default settings, main / guided: cabin 36.4 / 29.8; lamp hall 2.56 / 2.59; reflections 1.23 / 1.24. Mean indirect diffuse of the same draws: cabin +23 %, lamp hall -4.8 %, reflections -1.4 %. On the cabin walk, the median difference from the long stills moved from -10 and -8 % to +4 and +9 %. The likely cause is the slow history's clamp (ter-18hc), not yet measured.

Cost of the prototype at a half: the frame span rose by 0.35 ms in the bay and 0.39 ms in the walk, medians of 3 interleaved rounds at 1280 x 720, with no record of the GPU clock.

Review (one reviewer, read-only): no defect in the mixture, the counting rule, the Jacobian or P(q). Fixed: a probe assertion left in DescriptorLayoutsTests (B1, pinned at 16 sampled images, the spec's least); the docs said the off switch makes no light pyramid (F1); the GPU timings section missed the pass (F2); the docs named a ticket note that did not exist (F3, this note); no test held the shader's density against the copy (F4: OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, with a jitter argument in the copy); a known limit for off-screen light (F6); DepthTail* names renamed PyramidTail* (Q1); the LightPyramidImages property and pyramidLightAt (Q2); stale comments (Q3); the Simpson reason and "uniform point" (Q4); unused Width and Height, blank lines, the long bounce test in a nested class (Q6). Not done: Q5, one helper for the light that leaves a pixel in hitLightOf and the light pyramid. hitLightOf interleaves the direct part with the histories, and any guide keeps the estimate unbiased. B3 (weights reach the cap of 4 more often) and B4 (sky noise up to 1/(1-g)): the long stills stay within 0.8 % and the specks do not rise in the scenes measured.

Coverage of the changed lines: 419/425 lines, 97/104 branches. Uncovered: LightPyramid.Of's length guard (now ALightPyramidNeedsOneLightForEachPixel), Chord.Inside's beta = 0 branch (a ray parallel to a plane, measure zero), RenderTargets' refusal of a device whose RGBA16F cannot be sampled (every device can), and unreachable guards in ScreenTracePass and VulkanRenderer.

The prototype ran 43 mutants (22 killed, 20 survived, 1 build-failed). The table of 2026-11-10 below replaces it.

Finish, 2026-11-10 (branch ter-05wb-guided-rays-finish, main merged at 3cfc61c69). Logs, scripts and results: /private/tmp/tertius-guided-finish-20261109/. The survey is the uncommitted tool of ter-6k3k (worktree t3-3671a503) in scratch copies, with the light-signal split, at 1920 x 1080 with FSR 3.1 (render 1280 x 720). The bias and the lamp hall's film used the prototype's view probe (/private/tmp/tertius-motion-blotches-20261109/probe.diff) with a share switch.

The guided share: a quarter. Survey on main and three shares (blotch score of the still; motion blotch of the film):

| Case | Main | g = 1/2 | g = 1/4 | g = 1/8 |
|---|---|---|---|---|
| still bay-sunbeam | 0.085 | 0.034 | 0.041 | 0.052 |
| still docking-bay | 0.081 | 0.038 | 0.043 | 0.050 |
| still surface-terminal | 0.056 | 0.039 | 0.040 | 0.043 |
| still rect-lamps | 0.156 | 0.052 | 0.052 | 0.053 |
| still exit-cabin | 0.020 | 0.028 | 0.022 | 0.021 |
| still walk | 0.013 | 0.021 | 0.016 | 0.014 |
| still lamp-hall-4-spot-moving | 0.017 | 0.028 | 0.020 | 0.019 |
| film rect-lamps | 0.178 | 0.075 | 0.067 | 0.072 |
| film rect-bay | 0.354 | 0.399 | 0.385 | 0.378 |
| lamp hall bounce light, film draws 120 to 159 at 1280 x 720 | 0 | -4.8 % | -2.0 % | -0.9 % |
| lamp hall bounce light, survey reference at one exposure | 0 | -4.4 % | -1.7 % | -0.7 % |

Why a quarter: it keeps nearly all of the half's gain on the target stills (rect-lamps the same, bay-sunbeam 0.041 against 0.034) and loses less than half as much of the light outside the guide. It adds less noise to the calm views than a half. An eighth passes the lamp hall's 1 % limit, but it gives back about a fifth of the gain on bay-sunbeam and docking-bay. The lamp hall's loss is 2 % of a signal that is 4.7 % of its light, about 0.1 % of the image. So the blotches weigh more. Decision (Alexis, 2026-11-10): merge the guided rays now, at the share of 1/4, as an improvement. The acceptance items that failed stay open: the lamp hall's bounce light at -2.0 % against the 1 % limit; the stills bay-sunbeam 0.041, docking-bay 0.042 and rect-lamps 0.051 against 0.04; and the rect-bay film, whose score is light that trails a change, the target of step 6b. After the survey of all stills, Alexis also chose to merge with the still skin-hair at 1.04 specks per 10,000 pixels against the limit of 1.0 (main 0.99), as one more open item. Alexis also found the remaining mottle along the outline of rect-lamps' bright band of bounce light, and fine specks near its far lamps, clearly visible: ticket t7-3e22.

Review (one reviewer, read-only, on c26c72966). Fixed in 2323b10f5: B1, a diffuse ray walked its coded direction, so a guided ray near a target box's edge counted where its draw did not land and lost light (estimated 1 to 2 % at 1080p); a diffuse ray now walks its drawn direction, and only the record holds the code. F3: the density fetches a pixel's depth only where it has light and divides by the screen's light once per ray, and the descent draws three levels' units at once (the same bits). Q1: the comment on FinestSet's binding 0. Q2: the light pyramid reuses its down pipeline for level 1. F1, F2, F4 and Q4: doc statements, this note, and the placeholders. Answered: B2, the ray weight cap meets a ray with p-tilde 0 at 1 / (1 - g); the clamp-off long stills ran with the cap and stay within 0.5 %. Not done: Q3, the depth pyramid in RenderTargets and the light pyramid in ScreenTraceImages, because each lives with the images that its passes bind; the question on "29 of the 40" passes at ARCHITECTURE.md line 1077 stays open, because no test counts the graph with every setting on.

Acceptance of proposal section 2.2, on 2323b10f5 (g = 1/4):

| Item | Result | Verdict |
|---|---|---|
| Bias: long stills (clamp off, 1024 warm-up draws) against the cosine rays alone | bay 150/240/300/360: +0.00, +0.12, -0.03, +0.12 %; walk 40/86/110 (cap 512): -0.01, +0.20, +0.49 %; lamp hall +0.00 % | Pass |
| Lamp hall bounce light at the default configuration against main | film draws 120 to 159: -1.98 %; survey reference: -1.7 % | Fail, limit 1 % |
| Stills at most 0.04 | bay-sunbeam 0.041, docking-bay 0.042, surface-terminal 0.040, rect-lamps 0.051 (main 0.085, 0.081, 0.056, 0.156) | surface-terminal passes; the others miss by 0.001, 0.002 and 0.011 |
| Films: the motion blotch falls | rect-lamps 0.178 to 0.066, bay-sunbeam 0.420 to 0.404, lamp-hall-4-spot-moving 0.116 to 0.114, rect-bay 0.354 to 0.385 | Fail for rect-bay |
| No still's speck count above 1.0 per 10,000 pixels | All 90 survey stills on 2323b10f5, against main (the 9 stills of this run, the rest from the step-1 baseline survey): rect-lamps 1.35 to 0.82; skin-hair 0.99 to 1.04; every other still at most 0.97. Rises above 0.05: danube-hold 0.38 to 0.50, skin-hair 0.99 to 1.04, lamp-hall-16-hair 0.91 to 0.97, mirror-balls 0.61 to 0.83 | Fail for skin-hair, by 0.04; Alexis chose on 2026-11-10 to merge and keep it open (t7-3e22) |

The rect-bay film: its split by light signal says the film's bounce light is 1.84 times the still's at the middle frame on main and 1.98 times with the guide (level +0.84 and +0.98). So its score is light that trails a change, not noise, and the guide makes that trail a little larger. The proposal expected the score to fall. The bounce part's blotch: middle 0.141 to 0.151, end 0.115 to 0.145. The rect-lamps film's bounce part: middle 0.095 to 0.044, end 0.118 to 0.032. The bay-sunbeam film's bounce part: middle 0.186 to 0.161, end 0.118 to 0.104.

Speck counts of the prototype's rule (per 10,000 lit pixels, draws 120 to 159): cabin 36.4 to 31.8, lamp hall 2.56 to 2.58, reflections 1.23 to 1.24. Mean bounce light of the same draws: cabin +20 %, lamp hall -2.0 %, reflections -0.4 %.

Cost at 1296 MHz (p10 and p90 also 1296 MHz in every run), two frames in flight, 1920 x 1080, three interleaved rounds: frame span median bay 23.29 to 23.76 ms (+0.47), cabin walk 23.33 to 24.06 ms (+0.73); the passes from direct light through the resolve +0.62 and +0.88 ms. The F3 trims took the walk's sum from +0.96 to +0.88 ms. No Metal System Trace split the passes.

Coverage of the changed lines (Debug and Release, Rendering): 420/426 lines, 105/112 branches. Uncovered: LightPyramid.Of's length guard (now ALightPyramidNeedsOneLightForEachPixel); Chord.Inside's beta = 0 branch, a ray parallel to a plane, measure zero; RenderTargets' refusal of a device whose RGBA16F cannot be sampled, which no device reaches; the unreachable guards in ScreenTracePass and VulkanRenderer.

Mutants of 2323b10f5 (tools/mutate.py; no Core, Simulation or Physics line changed, so no Stryker run). The new tests kill S04 (WhenTheWalkRunsOutOfSteps...) and S05 (AGuidedRayThatABoxStops...), and the quarter share makes S02 killable:

| id | place | change | result | killed by or answer |
|---|---|---|---|---|
| S01 | Shaders/screen_trace.glsl:589 | no guiding without light dropped | killed | GuidedRaysRenderTests.WithNoLitPixelInViewEachBlocksDiffuseDensityIsTheCosineDensity |
| S02 | Shaders/screen_trace.glsl:590 | strategy pick flipped | killed | GuidedRaysRenderTests+BounceLightOverManyFrames.UnderAGraySkyTheFloorsLightKeepsItsMeanWithTheGuiding, GuidedRaysRenderTests+BounceLightOverManyFrames.AGlowingPatchsBounceLightOnTheFloorKeepsItsMeanAndLosesMostOfItsNoise, GuidedRaysRenderTests+BounceLightOverManyFrames.WhenTheWalkRunsOutOfStepsAGuidedRayBringsNoSkyLightAndTheFloorsLightKeepsItsMean |
| S03 | Shaders/screen_trace.glsl:629 | miss keeps cosine density | killed | GuidedRaysRenderTests+BounceLightOverManyFrames.UnderAGraySkyTheFloorsLightKeepsItsMeanWithTheGuiding, GuidedRaysRenderTests+BounceLightOverManyFrames.WhenTheWalkRunsOutOfStepsAGuidedRayBringsNoSkyLightAndTheFloorsLightKeepsItsMean, GuidedRaysRenderTests+BounceLightOverManyFrames.AGuidedRayThatABoxStopsBeforeItsTargetBringsNothingAndTheFloorsLightKeepsItsMean |
| S04 | Shaders/screen_trace.glsl:631 | guided miss keeps miss light | killed | GuidedRaysRenderTests+BounceLightOverManyFrames.WhenTheWalkRunsOutOfStepsAGuidedRayBringsNoSkyLightAndTheFloorsLightKeepsItsMean |
| S05 | Shaders/screen_trace.glsl:638 | uncounted guided hit counts | killed | GuidedRaysRenderTests+BounceLightOverManyFrames.AGuidedRayThatABoxStopsBeforeItsTargetBringsNothingAndTheFloorsLightKeepsItsMean |
| S06 | Shaders/screen_trace.glsl:499 | box depth doubled in density | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, GuidedRaysRenderTests+BounceLightOverManyFrames.AGlowingPatchsBounceLightOnTheFloorKeepsItsMeanAndLosesMostOfItsNoise |
| S07 | Shaders/screen_trace.glsl:510 | edges without jitter | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity |
| S08 | Shaders/screen_trace.glsl:513 | row planes flipped | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, GuidedRaysRenderTests.AGuidedBlocksDiffuseDensityHoldsTheCosineShareAndTheGuidedDensityOfItsHit, GuidedRaysRenderTests+BounceLightOverManyFrames.AGlowingPatchsBounceLightOnTheFloorKeepsItsMeanAndLosesMostOfItsNoise |
| S09 | Shaders/screen_trace.glsl:522 | Simpson middle weight | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, GuidedRaysRenderTests+BounceLightOverManyFrames.AGlowingPatchsBounceLightOnTheFloorKeepsItsMeanAndLosesMostOfItsNoise |
| S10 | Shaders/screen_trace.glsl:526 | area depth | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, GuidedRaysRenderTests+BounceLightOverManyFrames.UnderAGraySkyTheFloorsLightKeepsItsMeanWithTheGuiding, GuidedRaysRenderTests+BounceLightOverManyFrames.AGlowingPatchsBounceLightOnTheFloorKeepsItsMeanAndLosesMostOfItsNoise |
| S11 | Shaders/screen_trace.glsl:539 | 3x3 loses a row | survived | Judgment: a ray from the floor to the wall crosses only the hit pixel's box and those above it, so the row below adds nothing in the test scene; a ray from a ceiling would need its own scene. |
| S12 | Shaders/screen_trace.glsl:545 | counts without box | survived | Judgment: a guided ray that hits its target's 3 x 3 without crossing the target's box is rare; the copy's rule is pinned on the CPU (C13). |
| S13 | Shaders/screen_trace.glsl:448 | descent pick | survived | Judgment: the test panel glows evenly, so a skewed draw among its pixels moves the mean by less than the tolerance; the copy's descent is pinned by TheDescentDrawsEachPixelInProportionToItsLight. |
| S14 | Shaders/screen_trace.glsl:430 | levels share draws | survived | Judgment: levels that share draws skew the descent slightly over an even panel; no hook reads the guided targets. |
| S15 | Shaders/screen_trace.glsl:462 | target at mid depth | survived | Judgment: a target at the box's middle depth changes the guided density by a few percent inside a 5 % deep box, below the bounce tests' tolerance. |
| S16 | Shaders/screen_trace.glsl:475 | parallel outside plane | survived | Equivalent: a ray exactly parallel to a box plane has measure zero. |
| S17 | Shaders/light_pyramid_finest.comp:25 | surface light skipped | killed | GuidedRaysRenderTests.ASunlitSurfacesPixelsHoldGuideLight |
| S18 | Shaders/light_pyramid_finest.comp:31 | no cap | survived | Judgment: the cap acts only on a guide light above 32,256, which no scene makes; any guide keeps the estimate unbiased. |
| S19 | Shaders/light_pyramid_finest.comp:40 | padding holds light | survived | Judgment: the padding exists only when the image is larger than the viewport, under dynamic resolution, which no guided test runs. |
| S20 | Shaders/light_pyramid_finest.comp:22 | sky lit | survived | Equivalent: a sky pixel holds no emission and alpha 0 in the G-buffer, so its guide light is 0 either way. |
| S21 | Shaders/light_pyramid_down.comp:28 | down sums one row | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, GuidedRaysRenderTests.TheLightPyramidHoldsEachPixelsGlowAndEachCellTheSumOfTheCellsUnderIt, GuidedRaysRenderTests+BounceLightOverManyFrames.UnderAGraySkyTheFloorsLightKeepsItsMeanWithTheGuiding |
| S22 | Shaders/light_pyramid_down.comp:17 | child bound | survived | Judgment: the fetch past the image is undefined in Vulkan; on this driver it returns 0, so no test here can see it. |
| S23 | Shaders/pyramid_tail.glsl:74 | tail merges one child | killed | PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 10), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 5) |
| S24 | Shaders/light_pyramid_tail.comp:17 | tail max not sum | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, GuidedRaysRenderTests.TheLightPyramidHoldsEachPixelsGlowAndEachCellTheSumOfTheCellsUnderIt, GuidedRaysRenderTests+BounceLightOverManyFrames.AGlowingPatchsBounceLightOnTheFloorKeepsItsMeanAndLosesMostOfItsNoise |
| S25 | Shaders/screen_trace.glsl:415 | total from mip 0 | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, GuidedRaysRenderTests.AGuidedBlocksDiffuseDensityHoldsTheCosineShareAndTheGuidedDensityOfItsHit, GuidedRaysRenderTests+BounceLightOverManyFrames.AGlowingPatchsBounceLightOnTheFloorKeepsItsMeanAndLosesMostOfItsNoise |
| C01 | Passes/LightPyramidPass.cs:37 | off spelled | killed | GuidedRaysRenderTests.TheOffSettingBuildsNoLightPyramidAndGuidesNoRay, GuidedRaysRenderTests.WithTheSwitchOffEachBlocksDiffuseDensityInALitSceneIsTheCosineDensity, GuidedRaysRenderTests+BounceLightOverManyFrames.UnderAGraySkyTheFloorsLightKeepsItsMeanWithTheGuiding |
| C02 | Scene/GpuTypes.cs:871 | share without switch | survived | Judgment: with the switch off nothing writes the light pyramid, whose contents Vulkan leaves undefined; on this driver they read 0, so the shader guides no ray even with g above 0. |
| C03 | VulkanRenderer.cs:2179 | pass always | killed | GuidedRaysRenderTests.TheOffSettingBuildsNoLightPyramidAndGuidesNoRay |
| C04 | Frame/PyramidImages.cs:144 | level 1 reads mip 0 | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, GuidedRaysRenderTests.AGuidedBlocksDiffuseDensityHoldsTheCosineShareAndTheGuidedDensityOfItsHit, GuidedRaysRenderTests.TheLightPyramidHoldsEachPixelsGlowAndEachCellTheSumOfTheCellsUnderIt |
| C05 | Passes/LightPyramidPass.cs:48 | no barrier after finest | killed | GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity, GuidedRaysRenderTests.WithNoLitPixelInViewEachBlocksDiffuseDensityIsTheCosineDensity, GuidedRaysRenderTests.ASunlitSurfacesPixelsHoldGuideLight |
| C06 | Passes/ScreenTracePass.cs:170 | no barrier before rays | killed | GuidedRaysRenderTests.TheOffSettingBuildsNoLightPyramidAndGuidesNoRay, GuidedRaysRenderTests.WithTheSwitchOffEachBlocksDiffuseDensityInALitSceneIsTheCosineDensity, GuidedRaysRenderTests+BounceLightOverManyFrames.UnderAGraySkyTheFloorsLightKeepsItsMeanWithTheGuiding |
| C07 | Frame/ScreenTraceImages.cs:176 | resize keeps old pyramid | killed | (test run exit code 139) |
| C08 | Frame/DescriptorLayouts.cs:924 | pool storage images | survived | Judgment: MoltenVK allocates past the pool's sizes, so no test on this machine sees a smaller pool. |
| C09 | Frame/RenderTargets.cs:439 | format check sampled bit | survived | Judgment: every device samples RGBA16F, so no device reaches the refusal. |
| C10 | Shading/LightPyramid.cs:88 | descent tie | survived | Equivalent: reach equals the running sum only on a measure-zero draw. |
| C11 | Shading/LightPyramid.cs:84 | empty child skip | survived | Equivalent: a child without light is taken only when the unit reaches the total, which a unit below 1 never does, up to rounding. |
| C12 | Shading/LightPyramid.cs:164 | sky depth | survived | Equivalent: a pixel with guide light always has a surface, so its w is above 0. |
| C13 | Shading/LightPyramid.cs:210 | counts wider | killed | GuidedRaysTests.ASunlitPatchOnTheFloorLosesMostOfTheNoiseOfTheWallsBounceLight |
| C14 | Shading/LightPyramid.cs:232 | chord parallel | survived | Equivalent: beta = 0 is a ray parallel to a plane, measure zero. |
| C15 | Shading/LightPyramid.cs:236 | chord boundary | survived | Equivalent: alpha = 0 puts the origin on a plane, measure zero. |
| C16 | Shading/LightPyramid.cs:250 | Simpson C# | killed | GuidedRaysTests.TheGuidedDensityOfAPixelSumsOverAllDirectionsToThePixelsShareOfTheLight, GuidedRaysTests.TheGuidedEstimateOfAFloorsBounceLightKeepsTheCosineIntegralsMeanWithAMuchSmallerVariance, GuidedRaysTests.ASunlitPatchOnTheFloorLosesMostOfTheNoiseOfTheWallsBounceLight |
| C17 | Shading/LightPyramid.cs:266 | cell bound | killed | GuidedRaysTests.TheGuidedDensityOfAPixelSumsOverAllDirectionsToThePixelsShareOfTheLight, GuidedRaysTests.TheDescentDrawsEachPixelInProportionToItsLight, GuidedRaysTests.APatchBehindTheReceiversSurfaceAddsNothing |
| C18 | Shading/LightPyramid.cs:79 | one unit | killed | GuidedRaysTests.TheDescentDrawsEachPixelInProportionToItsLight, GuidedRaysTests.TheGuidedEstimateOfAFloorsBounceLightKeepsTheCosineIntegralsMeanWithAMuchSmallerVariance, GuidedRaysTests.ASunlitPatchOnTheFloorLosesMostOfTheNoiseOfTheWallsBounceLight |
| S26 | Shaders/screen_trace.glsl:605 | diffuse walks coded direction | survived | Judgment: the coded walk loses about h / (4 w) of the guided light at the box edges, about 0.5 % at the tests' 160 x 90, below the bounce tests' tolerance; the long stills at 1280 x 720 bound the fixed code within 0.5 %. |
| S27 | Shaders/screen_trace.glsl:429 | descent draws once | survived | Judgment: levels past the third reuse the first draw's units, as S14 shares them, which skews the descent slightly over an even panel; no hook reads the guided targets. |
| S28 | Shaders/screen_trace.glsl:493 | dark pixel early return | survived | Equivalent: a pixel with no light then fetches its depth and returns light times the integral, which is 0. |
| C19 | Passes/PyramidBuild.cs:98 | level 1 always down | killed | PyramidRenderTests.ATinyScreenBuildsItsFinestDepthWithoutATail(width: 1, height: 1), PyramidRenderTests.ATinyScreenBuildsItsFinestDepthWithoutATail(width: 2, height: 2), PyramidRenderTests.ATinyScreenBuildsItsFinestDepthWithoutATail(width: 2, height: 1) |
47 rows: killed 26, survived 21 (8 equivalent, 13 judgment calls).
