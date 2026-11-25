---
id: ter-pbxl
status: closed
deps: []
links: []
created: 2026-11-25T15:25:33Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-qdn8
---
# Give the station, ship and planet credible surface materials

Coordinate hull material ownership with the two fit tickets. Add spatially varied roughness and metallic response as supported, measured texture scale and surface relief, and mapped planetary terrain, ocean and clouds where appropriate. Respect source licences and existing asset conventions.


## Material evidence

The [material report](../docs/research/blue-danube-materials/README.md) records the Earth sources and the measured scale of the spokes, hub, galleries and cargo door. Matched renders hold the camera and Sun fixed and change the normals alone. All 31 asset tests passed in the assets dev shell. They check the cooked materials and tangents, axial tile size, cylindrical seam and slope direction. The report records the coverage and ten mutation outcomes.

The parent integrates EarthMeshRadius at both globe calls and owns the release film.

The scoped Release Host run passed 94 tests and skipped one opt-in measurement. The earlier full Checked log has no final Rendering or solution verdict.

The opaque station follow-up covers 107,806 triangles across the facade, floor, tunnels and strut. Five focused asset tests passed. All twelve glow copies retain their original emission and share their base normal image. The material report includes the matched hub tunnel closeup and sampled edge coverage.
