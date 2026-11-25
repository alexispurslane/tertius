---
id: ter-ozk7
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
# Meter scene light before bloom on every frame

The exposure meter reads the pre-exposed scene image and the frame graph currently schedules it before bloom. Verify the full data path, including feedback from one frame to the next.

## Acceptance Criteria

Render identical automatic-exposure sequences with bloom disabled, enabled and deliberately strong. Use a bright source next to a dark surface. Compare the meter histogram, target and applied exposure from the first draw through adaptation. Bloom must visibly change the halo while the exposure sequence stays identical.
