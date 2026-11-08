---
id: tiunr-3am9
status: open
deps: []
links: [tiunr-okzm]
created: 2026-11-08T05:53:28Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# A still's shown draw lights with a smaller local-light reach than its warm-up draws

Found 2026-11-08 under tiunr-okzm (/private/tmp/tertius-light-age-evidence-20261108/report.md). A local light's reach is sqrt(peak / E_cut), E_cut = pi R_cut / 2^highestStops (LightUnits.CutoffIrradiance), and highestStops depends on the draw's exposure step (ExposureResources.HighestStops). A headless still after 4 s gives each warm-up draw a step of 0.125 s and the shown draw 0 s (FrameHistory.StillExposureStep). In the reflections demo's low still the lamp's reach goes 2.75 m, 3.46 m, 4.30 m, shrinking to 4.13 m, then 3.31 m at the shown draw. The first still (endless step) has 728.8 m, and the first 60 Hz frame after it 2.31 m. So the warm-up draws light surfaces between 3.3 and 4.3 m from the lamp that the shown draw leaves unlit, and the histories hold that light. The light is below E_cut by design, so the image barely shows it, but the light-change age counts each flip as a change (about 83% of the low still's cuts). Decide whether the reach of a still's draws, and of the first frame after a still, follows one exposure step.

