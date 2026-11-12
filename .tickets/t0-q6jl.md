---
id: t0-q6jl
status: in_progress
deps: []
links: []
created: 2026-11-12T13:56:23Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Carry the retained deviation through the diffuse filter's feedback step

Design: docs/plans/bounce-light-confidence.md, section 22. Branch t7-3e22-retained-deviation. Step 2b.


## Notes

**2026-11-12T14:43:37Z**

2026-11-12, step 2b built. The variance dispatch marks the young estimate with a negative sign. The feedback step outputs kept·|σp| + Σ share·|σq| to step 2 and stores kept·max(σp, 0) + Σ share·max(σq, 0) into this draw's fast history alpha where its own length is at least YoungFrames (moved to diffuse_filter.glsl). Steps 2 to 6 keep the squared rule. DiffuseFilter has one wavelet set per parity and dispatch: Sets = 14. Every wavelet dispatch gets a barrier on the fast history, because the validation layers count each bound dispatch as a writer (a WAW hazard otherwise).
Tests (DiffuseFilterRenderTests): TheFeedbackStepStoresTheDeviationOfTheLightThatItMoves (red: 0), AYoungHistoryKeepsItsOwnRetainedDeviation (0.943), TheFilteredDeviationIsThatOfAMeanOfItsTaps now between 0.375^5 and 0.5^5 of the stored deviation (red 0.0050, green 0.0129; the plain kernel's 0.375^5 is a lower bound, because stops keep weight with the pixel).
