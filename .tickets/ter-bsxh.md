---
id: ter-bsxh
status: closed
deps: [ter-b2da]
links: []
created: 2026-11-02T01:38:36Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, environment-map]
---
# Environment map: a closed-form fill for the bounces after the first, from the geometric series

The user's request of 2026-11-01. Decision 17 of docs/plans/environment-map.md keeps the capture free of ambient light, so the map holds one bounce of the sun's, the lamps' and emission's light, and every surface that only bounced light reaches is black in the map. Interiors come out darker and harsher than with many bounces. The rejected alternative fed the previous map back into the capture: a chain of faces with no end, which accumulates over frames and needs warm-up refreshes. This ticket adds the missing bounces in closed form, within one frame and as a pure function of the map's six faces: light that bounces n times in an enclosure of mean albedo rho adds up to the first bounce's mean radiance times rho / (1 - rho), the geometric series, and the map's alpha already holds the open-sky share, so the series can run over the closed share only (an effective rho of rho times (1 - sky share)), which leaves open space unchanged. Design first, two designs for the user, for example: where rho comes from (a fixed setting against the faces' albedo, which needs a second face output or a spare channel), and where the fill enters (a uniform term in the prefilter's irradiance and specular levels, against the irradiance alone). Known cost of the closed form: it brightens only on average, so a corner that no light reaches gets the room's mean fill, not its own. Starts after epic ter-b2da, whose ter-8fz2 moves the diffuse light D to an irradiance cube, and whose comparisons need today's map. Updates decision 17, ARCHITECTURE.md and the snapshot references, each re-approved with its reference, actual image and FLIP heat map, and sends the user screenshots of lit interiors before and after.


## Notes

**2026-11-03T12:35:13Z**

Closed on 2026-11-03 at the user's request, without its work merged: it belongs to the pre-temporal renderer (spatial reuse, the 12 ms performance push, or the old reflection and bounce pipeline), and epic ter-89xq, the temporal renderer, supersedes it. Its notes and any branch or worktree stay for reference.
