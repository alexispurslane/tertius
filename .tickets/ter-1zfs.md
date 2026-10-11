---
id: ter-1zfs
status: closed
deps: []
links: []
created: 2026-10-11T18:48:00Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [rendering, slice, design]
---
# Choose the up axis of the hemisphere ambient light, which follows the camera's frame today

The hemisphere ambient light mixes AmbientLight.GroundColor and SkyColor by the surface normal's y in the axes of the camera's frame (lighting.glsl: mix(frame.ambientGround.rgb, frame.ambientSky.rgb, s.normal.y * 0.5 + 0.5)). So the same surface gets a different ambient light when the camera rides in a different frame.

Found by the acceptance read of the orbit scene (ter-fe04). In the far view (Earth frame, +Y is the north pole), the night side of the planet is brighter in the north than in the south (blue 15 to 16 at row 250, 9 at row 520). In the chase view (ship frame, +Y is radially out), the ship's top face gets the sky color and its back face less. World.Ambient defaults to sky 0.08 and ground 0.03.

Risk for the slice: in step 3 (ter-f8sh), the camera changes frame, and the shading of everything in view jumps.

Design to decide (two designs, then the user chooses): what up means for the hemisphere. Candidates: the root frame's axes, like the star field; the up of each surface's own frame; the direction away from the strongest gravity source; or one even ambient color in space scenes. Files: src/Tertius.Rendering/Shaders/lighting.glsl, src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs.

## Acceptance Criteria

A surface's ambient light does not change when the camera moves to another frame. A test shows the same surface from cameras in two frames that turn relative to each other, with the same ambient light.


## Notes

**2026-10-11T22:48:45Z**

Decided with the user on 2026-10-11. Two designs: (A) up is the root frame's +Y, one constant per frame shared with the star fix; (B) up points against the gravity of each surface's own frame: radial in a point field, against a uniform field inside its region (taken at the instance's center), an even mix without gravity, and a frame owner uses its own frame. The user chose B. After the review, the user also chose that only the field counts, not the frame's proper acceleration, so thrust never turns the up. The user also asked that hand-built render instances get no special default, so RenderInstance.Create takes the hemisphere as a required argument.
