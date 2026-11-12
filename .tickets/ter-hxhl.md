---
id: ter-hxhl
status: in_progress
deps: []
links: []
created: 2026-11-11T18:45:51Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Thin the diffuse filter: six steps of a 3 x 3 grid in place of five steps of a 5 x 5 grid

Step 7 of the renderer-finish proposal (/private/tmp/tertius-report-20261109/proposal.md, section 2.3, design A). The diffuse filter takes 4.18 ms on the deck, the second-largest cost after the light loop. Five steps of 24 taps give 120 taps per pixel (diffuse_filter_wavelet.comp). Six steps of a 3 x 3 grid with weights 1, 2, 1 along each axis give 48 taps. A sixth step, 32 pixels apart, restores the reach of 63 pixels.

## Design

Keep the edge tests, the rule that light moves and is not created, and the feedback of the first step into the slow history. Merge the variance dispatch into the accumulation's second dispatch, which reads the same images. Keep the old kernel as a configuration value (decision 2 of docs/plans/rendering-performance.md). Risk: more grain or a faint grid at the wide steps. The guided rays (ter-05wb) merged first, as the proposal required, because the remaining noise decides how much filter the image needs. Coordinate with the frame-time cuts (t6-h5e3), whose change 6 records the specular accumulation between the filter's steps.

## Acceptance Criteria

The filter takes about 2.2 ms on the deck in a Metal System Trace (estimate: about 2 ms saved). No survey still or film moves from pass to fail on blotch, specks or flicker, and no grid shows in the stills.


## Notes

**2026-11-12T06:58:44Z**

Outcome. The thin kernel is the default: six wavelet dispatches of a 3 x 3 B-spline, (1, 2, 1) / 4, at taps 1 to 32 pixels apart, 48 taps per pixel and a reach of 63 pixels. `TERTIUS_DIFFUSE_FILTER=wide` keeps the paper's kernel (five 5 x 5 dispatches, reach 62), and `off` still turns the filter off. Both wavelet stages compile `diffuse_filter_wavelet.glsl` with their own `KernelRadius`.

Frame time. Metal System Trace of the deck (`scripts/temporal-upscale-timing.json`, 120 frames from frame 1, 1280 x 720 upscaled to 1920 x 1080, `--no-counters`), two rounds that alternated main and the branch. Medians in ms:

| Build | Round | Filter | Diffuse accumulation | Frame span | Load at start |
|---|---|---:|---:|---:|---:|
| main d4a2d11f9 | 1 | 4.163 | 0.517 | 24.274 | 6.8 |
| thin kernel | 1 | 2.005 | 0.522 | 22.250 | 9.7 |
| thin, variance merged | 1 | 1.968 | 0.571 | 22.221 | 45.0 |
| main | 2 | 4.230 | 0.527 | 24.999 | 17.6 |
| thin kernel | 2 | 2.019 | 0.523 | 22.239 | 17.8 |
| thin, variance merged | 2 | 1.959 | 0.565 | 22.198 | 13.6 |

The filter fell by 2.16 and 2.21 ms, the frame span by 2.02 and 2.76 ms (round 2's main ran under load). 48 taps in place of 120 cost 48 % of the time, not 40 %.

The merge of the variance dispatch into the accumulation's spatial dispatch (design point of the proposal) was built, tested and traced. It moved about 0.04 ms from the filter into the accumulation: filter plus accumulation 2.527 and 2.542 ms with the separate dispatch, 2.539 and 2.524 ms merged. It needed an extra image of 8 bytes per pixel, a binding at 13 in every signal set, and its own spatial stage, because the validation layers count a statically used binding as a write even in a branch that never runs (sync hazards on the scratch image under the reflection-sharing prototype). For no gain, the branch dropped it (b7a0ddc57).

Survey (stills and films of the bounce-heavy views, main d4a2d11f9 against the branch; the branch's survey ran on 198f1a228, which held the merge; the merge changes no still after the first four draws, because it differs only where a history is younger than four frames):

| Still | Specks main -> branch | Blotch main -> branch | Verdict main -> branch |
|---|---|---|---|
| bay-sunbeam | 0.084 -> 0.084 | 0.0432 -> 0.0341 | fail -> pass |
| docking-bay | 0.405 -> 0.330 | 0.0462 -> 0.0377 | fail -> pass |
| rect-lamps | 7.954 -> 8.020 | 0.0583 -> 0.0537 | fail (specks, blotch) -> same |
| rect-bay | 0.010 -> 0.010 | 0.0139 -> 0.0106 | pass |
| windows-cabin | 0.036 -> 0.031 | 0.0226 -> 0.0152 | pass |
| docking-cabin | 0.289 -> 0.270 | 0.0285 -> 0.0248 | pass |
| exit-cabin | 0.212 -> 0.275 | 0.0231 -> 0.0200 | pass |
| cabin-light-cabin | 0.034 -> 0.039 | 0.0196 -> 0.0166 | pass |
| box-cabin | 0.034 -> 0.039 | 0.0196 -> 0.0166 | pass |
| surface-cabin | 0.054 -> 0.069 | 0.0199 -> 0.0174 | pass |
| pillar-room | 0.024 -> 0.019 | 0.0149 -> 0.0129 | pass |
| bounce-raised-box | 0 -> 0 | 0.0089 -> 0.0073 | pass |
| bounce-red-wall | 0 -> 0 | 0.0075 -> 0.0061 | pass |
| exit-deck | 0.020 -> 0.015 | 0.0069 -> 0.0060 | pass |

| Film | Flicker | Motion blotch | Verdict |
|---|---|---|---|
| bay-sunbeam | 0.0046 -> 0.0039 | 0.320 -> 0.331 | fail (blotch, level) on both |
| rect-bay | 0.0023 -> 0.0020 | 0.240 -> 0.248 | fail (blotch, level) on both |
| rect-lamps | 0.0011 -> 0.0010 | 0.063 -> 0.067 | fail (blotch) on both |
| docking-landing | 0.0018 -> 0.0016 | 0.183 -> 0.182 | fail (blotch) on both |
| exit-cabin | 0.0011 -> 0.0010 | 0.035 -> 0.031 | pass |
| exit-deck | 0.0004 -> 0.0004 | 0.011 -> 0.010 | pass |
| pillar-room | 0.0006 -> 0.0005 | 0.020 -> 0.017 | pass |

Daub score (/private/tmp/t7-3e22/daub.py of 2026-11-11 evening, with the 5 x 5 median; main -> branch): rect-lamps daub 0.0413 -> 0.0350, broad 0.0298 -> 0.0298; bay-sunbeam 0.0425 -> 0.0334, broad 0.0252 -> 0.0213; docking-bay 0.0505 -> 0.0432, broad 0.0206 -> 0.0177; rect-bay 0.0141 -> 0.0114; windows-cabin 0.0213 -> 0.0140; docking-cabin 0.0284 -> 0.0229; exit-cabin 0.0240 -> 0.0208; pillar-room 0.0491 -> 0.0462. Zoomed crops of the bay-sunbeam wall and floor and the rect-lamps wall show no grid; main's rect-lamps wall shows a faint checkered blotch that the branch does not.

Mutation testing (tools/mutate.py; no Core, Simulation or Physics change, so no Stryker run). S01 to S06 ran on the merged build, whose wavelet stage equals the final one; S03 to S06 mutated the merge's input code, which the branch dropped, so their rows no longer apply:

| id | place | change | result | killed by or reason |
|---|---|---|---|---|
| S01 | diffuse_filter_wavelet.glsl:19 | thin weights (0.6, 0.2) | killed | DiffuseFilterRenderTests.AYoungHistoryTakesItsNoiseFromItsNeighbors, LightThatMovesIntoAQuietRegionComesFromItsNoisyNeighbors |
| S02 | diffuse_filter_wavelet.glsl:47 | rows -1..0 only | killed | TheFilteredDeviationIsThatOfAMeanOfItsTaps, AYoungHistoryTakesItsNoiseFromItsNeighbors |
| S10 | diffuse_filter_wavelet.glsl:54 | no weight along y | killed | TheFilteredDeviationIsThatOfAMeanOfItsTaps, AYoungHistoryTakesItsNoiseFromItsNeighbors, AnEdgeOfLightKeepsItsShapeBesideNoise |
| S11 | diffuse_filter_wavelet.glsl:54 | x weight twice | killed | TheFilteredDeviationIsThatOfAMeanOfItsTaps, LightThatMovesIntoAQuietRegionComesFromItsNoisyNeighbors, ASpecksLightReaches...(Wide, 62) |
| C01 | DiffuseFilter.cs:277 | thin kernel five steps | killed | ASpecksLightReachesAsFarAsTheKernelsStepsTogetherAndNoFarther(Thin, 63) |
| C11 | DiffuseFilter.cs:171 | output after five steps | killed | ASpecksLightReaches...(Thin, 63) |
| C12 | DiffuseFilter.cs:283 | wide kernel six steps | killed | ASpecksLightReaches...(Wide, 62) |
| C09 | DiffuseFilter.cs:106 | default selects wide | killed | DiffuseFilterSwitchTests.WithoutTheSwitchTheFilterTakesTheThinKernel |
| C02 | DiffuseFilter.cs:107 | wide selects thin | killed | DiffuseFilterSwitchTests.TheWideSettingFiltersWithThePapersKernel |

9 rows: killed 9.

The survivor S04 of the first list (the merged input's unlit test) needs no answer: its line was in the merged variance code, which the branch dropped.

Review (reviewer agent): pass with minor changes, all fixed in 288f1321c. The wide kernel's reach test relied on half-float subnormals (about 4e-7 at 62 pixels), which a driver may flush; the test now lights a whole column, so the farthest pixel holds about 2.4e-4. The kernel type is a sealed class (a record let `with` make a third instance), and its constructor requires at most MostDispatches dispatches, the descriptor pool's sets. The switch also accepts `thin`. `tools/gpu-trace.sh` passes `TERTIUS_DIFFUSE_FILTER` on, so a trace can select the wide kernel. Stale comments (center weight 1/4, five later steps with a tolerance of 0.006) and the docs' kernel names are fixed.

Coverage of the changed C# lines (tools/coverage/changed-lines.py, Debug, the DiffuseFilter test classes): 31/31 lines and 6/6 branches. Tests: Checked 10,781 (all modules, before the review's fixes) and 3,081 (Rendering, after them), Release 10,769, 0 failed (2026-11-12).
