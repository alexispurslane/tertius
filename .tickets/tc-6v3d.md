---
id: tc-6v3d
status: open
deps: []
links: []
created: 2026-11-22T14:15:24Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The edge of a moving sun patch is saw-toothed in a film and smooth in a still, on main too

Found by the acceptance takes of Design X (t0-5tyj, 2026-11-22), on main c4c1f1f2f and on the line's head 57b4045ae alike. In the survey's film bay-sunbeam (demo-surface, bay-camera, from world tick 300, the sun's patch moves), the left edge of the sun's patch on the left wall (the bright slit at the top left of the view) is ragged in the film's middle frame and smooth in the still of the same state.

Measured on the full-size frames (1920 x 1080 output, render scale 2/3, so one render pixel is 1.5 output pixels), by docs/research/design-x-acceptance-2026-11-22/tools/slitedge.py: per output row 19 to 315, the edge is the leftmost column with 8-bit luma 250 or more, and the teeth are its residual from its own running mean over 31 rows.
- Films: residual 4.4 px from the 5th to the 95th percentile (M 4.4, X 4.5) and 6.5 px from lowest to highest, about 3 and 4.3 render pixels. 10 upward steps of 2 px or more, a median of 18 to 19 output rows apart (about 12 render rows), from 7 to 36.
- Stills: residual 1.5 px from the 5th to the 95th percentile, 2.4 px from lowest to highest, and no step of 2 px.
Crop, 3x nearest neighbor, M film, X film, M still, X still: docs/research/design-x-acceptance-2026-11-22/pics/film-bay-sunbeam-slit.png.

Not known: whether the teeth crawl from frame to frame. The survey keeps only the start, middle and end frames of a film, and no new take was made.

Candidates, not tested: (a) the shadow cascade's texel staircase under the one-sided filter of t8-rjno, where the patch clips to white; (b) the history's reset under the sun's motion.

