---
id: ter-xyfj
status: closed
deps: [ter-r0hc, ter-dv5t]
links: []
created: 2026-10-25T20:07:17Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-a24f
tags: [rendering]
---
# Bounce light: the sectors

Part 4 of docs/plans/screen-space-illumination.md: the sector shaders, the masks buffer and the setting's rename.


## Notes

**2026-10-26T01:01:12Z**

From Part 2 (2026-10-25): no GPU test covers the bounce term in occludedAmbient (+ lobes.diffuseColor * s.bounce) or the division of the estimate's rgb by exposureScale() in surfaceAt, because rgb stays 0 until this part. This part, or Part 6, adds a GPU check of both.
