---
id: tiunr-nuhk
status: open
deps: []
links: []
created: 2026-11-08T19:49:01Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-icn3
---
# Keep the tube light's inputs in one place in lighting.glsl

Review finding Q2 of 2026-11-08. The 1 cm floor of a tube's radius is written twice in lighting.glsl (near lines 298 and 504) and once in TubeView.cs. pathLight branches on TubeKind again and recomputes toCenter and the half segment that lightIrradiance already had. lightIrradiance has two tube-only out parameters, which scatteredLight declares and drops. Put the floored radius and toCenter in LightPath, so that a change to the tube's inputs touches one place. The rendered output must stay byte-identical: compare the SPIR-V and the tube light render tests.

