---
id: ter-13rm
status: closed
deps: [ter-vges, ter-gayt]
links: [t1-mybi, ter-4iic, t2b-d2m6, ter-6fka]
created: 2026-11-03T12:30:05Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# World-space radiance cache fed from the screen

**2026-11-25 · Retired.** The second-cut review does not require this feature or project to reproduce the three priority defects. Keep the design as a reference.

The user's choice of 2026-11-03: a later step of the temporal renderer. Visible surfaces write their light into a sparse world-space cache each frame, and a screen ray that misses reads the cache before the environment map, so that light behind the camera stays known for a while. It needs a design of its own.


## Notes

**2026-11-22T21:13:04Z**

2026-11-22, orchestrator: the lighting system review (docs/research/lighting-system-review-2026-11-22/README.md) rejects this ticket's premise. A store that only the screen feeds remembers what the eye saw, so the light would depend on where the camera has been. The step moves to ter-6fka, whose store is written by views that do not depend on the eye. This ticket stays open until ter-6fka's experiments decide, and then closes or is rewritten.
