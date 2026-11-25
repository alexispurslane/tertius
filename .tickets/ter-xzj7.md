---
id: ter-xzj7
status: open
deps: []
links: []
created: 2026-11-25T22:58:22Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-buzm
tags: [rendering, blue-danube]
---
# Settle the grain on the berth deck without losing its material detail

The wide locked berth view around 7:27 shows speckle across the dark deck and in the area near the cargo. The old coplanar surface was removed in ter-22b9. Start with the same camera and original asset, then compare material and geometric normals, direct light, diffuse bounce, reflected light, and final output.

## Acceptance Criteria

On an uncompressed sequence, follow a fixed patch of deck and its reflected image where visible. Record the frame-to-frame variation, settled brightness and a close crop of the joints. Identify the first pass with the objectionable pattern. Show matched moving clips retaining the joints and lighting, plus the GPU time excluding readback.
