---
id: ter-4oow
status: in_progress
deps: []
links: []
created: 2026-11-10T00:34:14Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Share a block's reflection ray with each pixel whose reflection lobe holds it

Step 2 of docs/plans/renderer-finish.md (proposal of 2026-11-09, section 3). Today every pixel with a roughness up to 0.5 traces its own reflection ray in every draw (screen_trace_plan.comp, TERTIUS_PRECISE_SPECULAR_HISTORY). Alexis chose design B on 2026-11-09: a pixel uses its block's reflection ray when that ray lies inside the pixel's own reflection lobe, that is, when the two pixels' normals differ by less than a share of the lobe's width. A history younger than FullRateFrames keeps its own ray. The uncapped precise reflection history, the light-change age and the debug switch TERTIUS_SPECULAR_HISTORY stay.

## Acceptance Criteria

Deck reflection-ray pass at most 2.7 ms in a Metal System Trace (main 9.65 ms). Rough plate noise at most 7.7 W/(m² sr) (main 5.5). Smooth plate noise and mirror blur reported against main. No survey measure of the reflection stills and films moves from pass to fail. If mirrors blur visibly or the pass misses 2.7 ms by more than 1 ms, stop before the merge.


## Notes

**2026-11-10T11:15:06Z**

Measurements so far (2026-11-10). Deck trace (scripts/temporal-upscale-timing.json, 120 frames, Release, 1920x1080 with FSR 3.1): reflection rays median 2.65 ms at LobeFitShare 0.25 and 0.5 (main 9.65 ms in the report of 2026-11-09), ray plan 0.85 ms (main 0.45), frame span median 24.49 and 24.50 ms (main 31.23 ms today), GPU clock 1291 to 1293 MHz. The plate test of 2026-11-07 no longer measures ray noise on main: since 972d42f16 the emitter holds a tube light, so its glow reaches no ray. A scratch copy of the tool without the tube light reproduces main's rough plate exactly: noise 5.529, error 29.022 W/(m² sr). With the lobe fit at 0.5: rough noise 14.02 (limit 7.7), error 27.08; smooth noise 2.85. The rough plate's reflection core shows a visible mottled grain. The first version compared normals only; it shared a flat mirror's rays at the roughness floor, and a glowing panel's mirror image lost 3 to 5 % of its light to the weight cap of 4 (ScreenTraceRenderTests). The rule now compares the two pixels' mirror directions.

**2026-11-10T12:51:57Z**

Not merged (Alexis, 2026-11-10): the rough plate noise 14.02 fails the limit of 7.7, so Alexis decides on the real stills. Survey stills of main and LobeFitShare 0.5 at 1920x1080 with FSR 3.1: FLIP of share 0.5 against main, mean and share above 0.1: reflections-front 0.0049 / 0.08 %, reflections-low 0.0163 / 3.16 %, reflections-edge 0.0034 / 0.02 %, mirror-balls 0.0064 / 0.08 %, mirror-plate 0.0054 / 0.02 %, exit-deck 0.0109 / 0.00 %, exit-floor 0.0085 / 0.00 %. Images in /private/tmp/ter-4oow/decision/.
