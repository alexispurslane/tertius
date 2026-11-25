---
id: ter-5uxs
status: open
deps: []
links: []
created: 2026-11-25T23:31:04Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-buzm
tags: [rendering, blue-danube]
---
# Set bloom strength from scene brightness before HDR storage clips highlights

The bloom prefilter currently thresholds a pre-exposed image. Some surface lighting has already been capped for half-float storage before that read. Measure the contributing scene radiance upstream of the first lossy cap. Calibrate how much light spreads around a highlight using its brightness, with the final composite after metering and before display tone mapping.

## Acceptance Criteria

Compare a sunlit matte panel, an emissive lamp and a brighter source of the same apparent size at fixed exposure. Include two source levels that produce the same capped center pixel. Measure halo brightness and extent against source level, preserve visible hull and window detail at ordinary daylight levels, and verify finite outputs. Record the chosen bloom response and its units.
