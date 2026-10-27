---
id: ter-7s3g
status: in_progress
deps: [ter-izsu, ter-uhd8, ter-ehxp]
links: []
created: 2026-10-25T20:07:17Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-a24f
tags: [rendering]
---
# Bounce light: the cost, the captures, the defaults and the docs

Part 7 of docs/plans/screen-space-illumination.md, after the cut of ter-izsu.


## Notes

**2026-10-27T03:44:50Z**

Part 7 captures and cost are done on ter-a24f-ssgi (fba0eab8). Sectors pass at 3 steps: 1.41 ms (R 0.5 m) to 2.00 ms (R 2 m); 6 steps add 1.0 to 1.5 ms. R 1.457 m with 3 steps changes the stills about as much as R 1 m with 6 steps, for about 1 ms less. R 2 m shows grain in dark areas. Every setting shows soft bright blotches on the wall beside demo-exit's cabin lamp. Hypothesis under test: the lamp panel's emission, which equals its point light's power, bounces a second time, and its small bright area gives sparse hits. The defaults wait for that test.
