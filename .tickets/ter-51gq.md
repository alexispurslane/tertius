---
id: ter-51gq
status: closed
deps: []
links: []
created: 2026-10-26T15:28:28Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, performance]
---
# The lighting pass costs 0.49 ms more with a map per pixel

Measured in ter-uy7y Part 4 (ARCHITECTURE.md, 'The environment map', 'Cost'), demo-exit.json at 1920x1080 in Release on the M1 Max: the lighting pass grows from 1.72-1.77 ms to 2.24-2.25 ms with the maps per frame, ten times the 0.05 ms that docs/plans/environment-maps-per-frame.md expected, and the transparent pass by about 0.075 ms. Two probes of one run each: with s.map fixed at 0u in surfaceAt (gbuffer_surface.glsl), the lighting pass took 1.803 ms, the base's time; with inMapAxes returning v unturned and the map still read from the G-buffer, it took 2.200 ms. So the cost follows the per-pixel varying map index (the cube array layer, the irradiance offset 9*map, or the value's live range through the shader), not the two quaternion turns. Find the cause with tools/gpu-trace.sh and bring the pass back near the base, without breaking the map code or the portable rules.


## Notes

**2026-10-26T18:19:14Z**

2026-10-26: merged (645162a4). Cause: the nine irradiance coefficients read at a per-pixel map took registers per invocation and cut occupancy (13.2% to 17.2% after). Fix: a waterfall loop over subgroupBroadcastFirst(map); the lighting pass fell about 0.42 of the 0.49 ms under load; captures byte-identical. New device requirement: subgroup BALLOT in fragment and compute. Coverage run skipped: the only new C# is the pure MissingSubgroupSupport, whose four outcomes are tested and hand-mutated, and the property read that every device creation runs.
