---
id: t2-hvez
status: open
deps: []
links: []
created: 2026-11-09T12:12:54Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-icn3
---
# The light-change count of 4 draws lost its reason when the shadow tap quarter went

LightChangeDraws in Shaders/screen_trace.glsl is 4 because the shadow filter's turning tap quarter under FSR cycled a penumbra's light with a period of up to 4 draws. ter-yfb5 removed the quarter: every view now takes all 16 taps. FSR's jitter still moves the light at silhouettes and thin steps. Measure whether a smaller count (2, or 1) keeps the reset counts of the demo's still reflections scene (ARCHITECTURE.md, the light-change age) and responds faster to a real change. Keep 4 if it does not.

