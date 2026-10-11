---
id: ter-ujbt
status: open
deps: []
links: []
created: 2026-10-11T18:48:00Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [rendering, slice]
---
# Fix the star field to the root frame's axes, not to the axes of the camera's frame

The star field turns with the frame that the camera rides in. The snapshot gives the camera's rotation in its own frame (RenderSnapshotBuilder.Build: Rotation = cameraPose.Rotation), SceneBatcher.WriteConstants builds cameraForward, cameraRight and cameraUp from it, lighting.vert builds the view ray from those axes, and lighting.frag calls starField(direction) with no further rotation. So the stars are fixed to the axes of the camera's frame. The shader comment says that they are fixed to the world axes.

Found by the acceptance read of the orbit scene (ter-fe04). The chase camera rides in the ship frame, which turns once per orbit because it faces Earth. In chase-0.png to chase-4.png, every sky pixel above row 304 is identical, although the ship turns 90 degrees per quarter. The sun moves correctly, so it drifts against the stars. Pressing V (to the far view, in the Earth frame) changes the sky.

Risk for the slice: in step 3 (ter-f8sh), the camera moves from the ship frame to the Earth frame, and the whole sky will jump by the ship's rotation.

Fix proposed by the review: carry the rotation of the root frame relative to the camera's frame in the snapshot and the frame constants (from relCurrent of the root, interpolated like the camera rotation). In lighting.frag, rotate the view direction by its inverse before starField. Files: src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs, src/Tertius.Rendering/Scene/SceneBatcher.cs, src/Tertius.Rendering/Shaders/lighting.vert, lighting.frag.

## Acceptance Criteria

With the camera in a frame that turns relative to the root, the stars stay fixed to the root frame's axes: a snapshot test of the same direction from two camera frames shows the same stars, and the orbit's chase captures show the stars sweep by the ship's turn. Switching the view between frames does not move the stars.

