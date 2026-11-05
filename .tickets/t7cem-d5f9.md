---
id: t7cem-d5f9
status: open
deps: [ter-7tnv]
links: []
created: 2026-11-05T19:20:23Z
type: chore
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, environment-map]
---
# Remove unused environment-distance producers after automatic box projection

Automatic semantic-frame box projection in ter-7tnv removes every production distance-cube reader. The old distance cube is still allocated, written by environment faces, downsampled and bound. Removing that producer crosses image allocation, attachment formats, passes, prefilter sets, layouts and producer tests, so it must not delay the cheap reader replacement.

## Acceptance Criteria

Remove the unused distance images, writes, downsampling, bindings and obsolete producer tests. Preserve map radiance, irradiance, sky and all public render interfaces. Run Checked and Release suites and Vulkan 1.3 shader validation. Measure matched whole-frame costs with GPU-clock provenance rather than assuming a saving.
