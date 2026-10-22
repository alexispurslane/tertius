---
id: ter-7oi8
status: open
deps: [ter-c3rf]
links: []
created: 2026-10-22T12:58:26Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [core, rails]
---
# Kepler attitude as a union

KeplerOrbit keeps AxialTilt, SpinRate and SpinPhaseAtEpoch beside Attitude, and they mean something only for KeplerAttitude.Spin (docs/plans/union-tooling.md, section Closed sets that are not unions yet). Make the attitude a union of a spin case and a facing case.

