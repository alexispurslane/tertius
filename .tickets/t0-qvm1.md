---
id: t0-qvm1
status: open
deps: []
links: []
created: 2026-11-22T11:13:56Z
type: task
priority: 3
assignee: Alexis Purslane
parent: t7-3e22
---
# The region's drift test fires falsely at anchors whose light scatters with rare bright draws: 8 to 14 % of the draws beside the lamp hall's windows

Found by the room's takes, part B (docs/research/t0-9fo0-room-after-shadow-fix/README.md, 'The region test's fires'). In the still rect-lamps on the line's head 6d8a68332, the bounce light's region test (region_drift.glsl) fires in 1.22 % of the tested anchor-draws after draw 133, where the light does not move. 230 of the 240 anchors fire at 0.78 to 0.83 %, which is this test's rate on Gaussian noise: it estimates its spread from 18 draws, so 3 standard deviations give about 0.8 %, not 0.27 %. The 10 busiest anchors, all at the side walls' windows, fire at 8 to 14 % of the draws. Their region's light scatters by 11 to 17 % from draw to draw, against a median of 5.2 %. A random order of their draws fires as often, so the spread causes the fires, not a trend. Each false fire caps the bounce history of the blocks in the anchor's region at 8 draws for one draw. No picture and no score shows a fault there: the walls' daub is 0.0118 with the remedy and 0.0116 without it. So this is not needed for the merge of Design X. Build it only if a scene shows more noise near such anchors.

Two ways that the room's lead named, not tried: a spread from all 36 draws of the ring, or a spread that ignores the brightest draws. A third: a tap that is the mean of a block's samples, not one pixel. First find which pixels of those regions cause the spread. The replay script regionnoise.py of the record gives every logged answer exactly, so a changed test can be tried on the logged light without a build.

