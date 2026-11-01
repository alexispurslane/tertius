---
id: ter-efa0
status: open
deps: []
links: []
created: 2026-11-01T18:14:47Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Async compute: run the shadow maps, the local shadows and the environment faces beside other work on a second queue

From ter-prio's profile (2026-11-01): the shadow maps, the local shadows and the environment faces use under 7 % of every GPU unit for about 0.7 ms per frame. A second queue could fill part of that time with other work. ter-prio's upper bound is 0.3 to 0.5 ms per frame, not measured. Vulkan offers queues of several families, and a device without a separate compute queue must keep the single-queue path. Waits for the pause of ter-pq5y to end (ter-b2da).

## Acceptance Criteria

A measured saving on demo-exit with traces of every frame, the image bit for bit the same, and a fallback to one queue that a test pins.

