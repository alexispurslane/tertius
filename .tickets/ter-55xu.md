---
id: ter-55xu
status: in_progress
deps: []
links: []
created: 2026-11-02T12:51:56Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, environment-map]
---
# The distance cube: each environment map stores its inverse distances and their levels

Ticket 2 of ter-jw7g's design (docs/plans/environment-map-distances.md, 'Ticket 2' and 'The distance store'). An R16F cube per map with 1000 m / distance (the sky 0), written as a second attachment of the face pass, with solid-angle-weighted mips beside the radiance mips, binding 16, and the barriers. Acceptance: GPU tests 13 to 15 pass in Checked and Release; no reader reads it yet, so the captures of demo-surface, mirror-balls, pbr-spheres and cabin-light are byte for byte main's; validation shows no new message; the memory matches the design's 'Cost'.


## Notes

**2026-11-02T12:54:15Z**

2026-11-02: launched as workflow run 43935f37-7349-43d9-bbba-f81c2831249d, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (build, review, fix), branch ter-55xu-distance-cube, base 6c40880fa.
