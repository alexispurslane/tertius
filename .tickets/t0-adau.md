---
id: t0-adau
status: open
deps: []
links: []
created: 2026-11-14T15:56:48Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# The rect-lamps ceiling shows single bright pixels near the far lamps; find their cause

Filed 2026-11-14 at the orchestrator's request. The long reference (clamps off, a long run) shows the same specks, so they are not a fault of the history or its clamp. Hypothesis to test first, stated as a hypothesis: a ceiling point's bounce ray that hits the glossy floor reads the floor's shaded light as the camera sees it, including the lamps' bright reflections in the floor. That light depends on the view direction, and the ceiling point does not receive it from that direction. Cheap test: count specks in a rect-lamps still with the hit light's specular part left out, against main and against the unchanged branch, on one machine. No fix this week unless the test is clear and the fix is small.


## Notes

**2026-11-14T16:19:02Z**

2026-11-14, first test (cloud, files in /tmp/t7-3e22/preview): the floor-reflection hypothesis does not hold. Bounce rays already leave the hit's specular part out (the diffuse layer of leavingLightOf); leaving it out of the reflection rays too removed about 10 % of the specks, and the rest stay at the same pixels (specks per 10,000 pixels at 132 draws: 6.15 with the knob, 6.74 without, main 7.29); the ceiling daub moved by less than 0.001. The long reference (clamps off) shows a far denser field of fireflies over the bright band than any shown still, so the specks are rare bright samples in the light itself that 20 captures do not average. Next hypothesis to test, as a hypothesis: which hits make them (for example a bounce ray that reaches a lamp's own glow on screen, glowAt in leavingLightOf), by a speck map of the hit kind.

**2026-11-14T16:45:21Z**

2026-11-14, robust reference of rect-lamps (median of 24 batch means of 20 captures from one clamps-off run of 16,467 draws; /tmp/t7-3e22/robust): with the fireflies gone, the true ceiling shows a broad bright glow column around the lamps. Every shown column (main, Design X, the stand-ins) is much darker there and shows specks in its place. So the specks look like the few surviving samples of a real, rarely sampled light that the clamps and filters otherwise remove: the fault is likely the sampling of that light, not a speck to suppress. Next: measure the glow column's level in each column against the reference, and find which path carries that light.
