---
id: ter-egcv
status: open
deps: [ter-4xw0]
links: []
created: 2026-11-01T01:45:25Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, tests]
---
# Render test: the local shadow pass ranks shadow tile holders with the scene's camera

Found by the orchestrator's hand mutations of ter-4xw0 (2026-10-31). Mutant w14 changes LocalShadowPass's call of ShadowTiles.Update from 'camera: scene.Camera' to 'camera: default', and every test still passes. The ShadowTiles and SpotLampView tests call Update with the camera of their own PreparedScene, so no test covers the pass's wiring.

What kills it, by reading CameraView: an empty camera has five zero planes, so Culls is false and EyeDistanceToAxisInView returns a distance for every spot lamp. Under the mutant every spot holder therefore grows into InView. A render test that kills it: a renderer with LocalShadowAtlas Tiles = 1, two frames. In frame 1 a spot lamp whose axis enters the view takes the one tile. In frame 2 its reach shrinks, so that even at sqrt(2) times the reach its axis misses the view, and a dimmer spot lamp whose axis is in view stands over a box. Assert that the box shadows the floor in frame 2 (as ALightPastTheBudgetLightsWithoutAShadow reads it). SpotLampViewTests.AHolderWhoseGrownReachStillMissesTheViewWithItsAxisLosesItsTilesToALampWhoseAxisIsInView has the geometry at the ShadowTiles level.

## Acceptance Criteria

A render test fails under mutant w14 and passes on the real code; the ter-4xw0 note names it.
