---
id: ter-pn1w
status: closed
deps: [ter-s6zr]
links: []
created: 2026-11-02T12:51:56Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, environment-map]
---
# Each environment map face keeps the eye that drew it

Ticket 5 of ter-jw7g's design (docs/plans/environment-map-distances.md, 'Ticket 5' and open question 1). The user chose option 2 on 2026-11-02. RenderChainFrame gains Origin and PreviousOrigin; the batcher and the frame constants carry each face's eye offset; mapHit and MapSearch use the offsets. Acceptance: AWalkingCameraKeepsANearWallsReflectionInPlaceWhileTheFacesAge passes, red first; a still keeps its bits; the batcher's CPU benchmark shows no allocation.


## Notes

**2026-11-03T12:35:12Z**

Closed on 2026-11-03 at the user's request, without its work merged: it belongs to the pre-temporal renderer (spatial reuse, the 12 ms performance push, or the old reflection and bounce pipeline), and epic ter-89xq, the temporal renderer, supersedes it. Its notes and any branch or worktree stay for reference.
