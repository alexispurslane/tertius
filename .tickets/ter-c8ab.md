---
id: ter-c8ab
status: open
deps: []
links: []
created: 2026-10-11T22:00:39Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering]
---
# Keep sunlight off impostor captures of objects inside a closed hull

In free play the renderer may draw a small or far object as an impostor captured at render time. A capture lights the object with the sun but has no shadow maps: only the far-shadow spheres block the sun there, and a sphere never shadows points inside it. So an object inside a closed hull, such as the orbiting ship's cabin, gets full sunlight in its capture while the walls and floor around it are dark. Today the cabin holds only the lamps, whose albedo is black, and the person, whose box holds the camera, so nothing shows it yet. The next steps of the vertical slice will put objects in the cabin.

Design to decide: for example a capture that takes the sun's visibility at the object from the shadow maps, or a frame setting that says its interior gets no sunlight, or NoImpostor for such contents. Scripts draw geometry, so a test needs impostors on.

## Acceptance Criteria

An object inside the closed cabin, drawn as an impostor in free play, gets no sunlight that the hull blocks, and a test with impostors on shows it.

