---
id: ter-oa4b
status: open
deps: []
links: []
created: 2026-10-16T12:38:20Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, demo]
---
# Start the orbit scene's ship with a half-step velocity correction, so that its orbit stays circular

The Sonnet reader of orbit-cluster found that the ship loses 21 m of radius in 300 s (0.0723 m/s inward), with constant angular momentum: the frame step is semi-implicit Euler, and the ship starts with the exact circular velocity instead of the velocity half a step earlier. Relative motion is unaffected (the crates match Clohessy-Wiltshire to 0.4 mm). Give the start velocity a half-step correction in DemoScenes and check the radius over an orbit.

