---
id: ter-8fz2
status: open
deps: [ter-k96l]
links: []
created: 2026-11-01T18:27:21Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, environment-map]
---
# Environment map: the diffuse light D from an irradiance cube convolved from the specular map, in place of band-2 spherical harmonics

User decision, 2026-11-01, after ter-xgra. The map's diffuse light D is stored as band-2 spherical harmonics, and the specular map holds the map's light per direction. Under a small bright light the two disagree far more than the 'few percent' that Known limit 2 of docs/plans/screen-space-illumination.md states. Evidence: captures/xgra/sheets/deck-late-artifact2-candidates.png, panels off and off-bruteD. With the screen-space light off, a brute-force D (256 cosine-weighted reads of the specular map at level 0) makes the cabin's base face on deck-late clearly lighter. The error is in every pixel's ambient light, not only in creases. The fix: D comes from an irradiance cube, convolved from the same specular map that the reflections and the screen-space light read, and read at the pixel's full-resolution normal so that normal maps keep their detail. ter-xgra's probe patch p-bruteD.patch (captures/xgra/probes) is the reference. Cost: a new convolution pass for each map, estimated near the 0.28 ms of today's prefilter, not measured. Comes after ter-k96l, whose share form D(n) · S uses D. The design follows docs/plans/environment-map.md, and its decisions on the spherical harmonics change with it.

## Acceptance Criteria

D comes from the irradiance cube, and the spherical harmonics are gone or kept only where the ticket states a reason. On deck-late with the screen-space light off, D lies within a stated tolerance of the brute-force D (p-bruteD.patch) on the cabin's base face and the floor band. A render test pins that a surface under a map with one small bright light gets the cube's irradiance within that tolerance. The cost is measured with interleaved runs and a control. The convolution keeps the map's compute pipelines within 4 storage images per stage. ARCHITECTURE.md, environment-map.md and Known limit 2 of screen-space-illumination.md match the code.

