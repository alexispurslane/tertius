---
id: ter-s6zr
status: open
deps: [ter-0pj8, ter-55xu]
links: []
created: 2026-11-02T12:51:56Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, environment-map]
---
# The corrected read: every reader of the ambient specular term reads the map where its ray ends

Ticket 3 of ter-jw7g's design (docs/plans/environment-map-distances.md, 'Ticket 3', 'The search' and 'Every reader'). mapHit, the corrected environmentSpecular through ambientTerms, ShadingInput.position, and the specular occlusion over the whole corrected term (the user's answer A to open question 2). A ray that meets no stored surface reads exactly today's texel. Acceptance: GPU tests 16 to 21 pass in Checked and Release, each red first; changed reference images reviewed with the three images; screenshots of the terminal floor, the floor film and pbr-spheres before and after.

