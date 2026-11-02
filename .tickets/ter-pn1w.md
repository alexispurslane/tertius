---
id: ter-pn1w
status: open
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

