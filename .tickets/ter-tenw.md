---
id: ter-tenw
status: closed
deps: []
links: [ter-4psc, ter-me1y]
created: 2026-10-17T07:05:17Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, frames]
---
# A ghost in a semantic child's own system never reaches the child's numeric frames

From ter-2gg0 (2026-10-17): a ghost that the ghost pass places in a semantic child D's own system (a hull that does not fit, or a body that touches D's bubble) never reaches D's numeric frames, so a fast crate inside D in its own falling frame never meets it. Without numeric frames it would. Apply the stick-out rule of ter-2gg0 one level down.


## Notes

**2026-10-18T01:54:51Z**

Fixed by ter-4psc (2026-10-18): a body of a semantic frame that may touch the ghost of a parent body or of a sibling's body stays in the frame's own system, where physics keeps that ghost. Tests: FrameTreeGhostPlaceTests, GhostPlaceTests (a fast crate inside a frame meets the ghost of a hull that touches the bubble without entering).
