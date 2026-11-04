---
id: ter-jxjx
status: open
deps: []
links: []
created: 2026-11-04T18:20:05Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
---
# The screen tracer's design after the spikes: V6, cap 62, full rays, the 12 ms budget

Design prerequisite of ter-gayt (build ticket 4). docs/plans/temporal-renderer.md sections 6, 11 and 12 and the decisions that ter-gayt names predate the lean trace spike (ter-8hbi), the profiling spike (ter-sftw), the GPU calibration (ter-3ug8), the removal of light sampling (ter-9nrc) and the user's 12 ms target. One design agent updates the plan with the settled results and writes ter-gayt's order of work: the walk (V6's two-depth step for both kinds in one walk and one kernel, over DepthPyramid with its near channel and a finest level), the caps (reflection 256, diffuse 62), 10 % own rays, the record read in the words form, the budget under 12.0 ms (2.94 to 3.76 ms for the two dispatches on the deck, from ter-gayt's note of 2026-11-04), and the user's rule for the first build: full rays, measure the real frame at the top clock, add a ray limit only if the frame stays over 12 ms after the hill-climb (ter-dzxf). No code.

