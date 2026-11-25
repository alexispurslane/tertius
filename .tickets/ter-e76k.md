---
id: ter-e76k
status: open
deps: []
links: [ter-qbw8]
created: 2026-11-25T23:31:04Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-buzm
tags: [rendering, blue-danube]
---
# Resolve jagged nearby shadows cast by distant lights within the GPU memory budget

Reproduce a distant light illuminating a surface close to the camera. Identify the actual shadow map, tile or cascade and the area one shadow texel covers on that surface. Measure Linux Runpod GPU memory use before selecting resolution, allocation or filtering changes.

## Acceptance Criteria

Record the device-local memory budget, peak use and remaining headroom through representative film shots, including peak shadow allocation. Report current map dimensions, format, layers and the additional MiB for each candidate. Compare matched close-ups and moving shadow edges at the same light distance. Verify contact detail, edge stability, peak memory and GPU time on Linux Runpod.

## Notes

**2026-11-25T23:31:29Z**

Use the peak and headroom measurements from ter-qbw8 when comparing proposed shadow allocations. Shadow-edge reproduction and memory measurement can start independently.
