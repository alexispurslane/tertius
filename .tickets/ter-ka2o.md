---
id: ter-ka2o
status: closed
deps: [ter-upyg]
links: []
created: 2026-10-12T10:49:17Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [rendering, demo, slice]
---
# Vertical slice: transparent windows in the ship, to stress near and far rendering

The user asked for transparent windows in the ship, not empty holes, to stress the renderer's near and far handling: a pane a few meters from the eye with Earth and the sun behind it crosses every depth range. The renderer has one forward pass for transparent objects, sorted back to front across ranges, which tests occlusion against the distance target. The slice does not use it yet.

Defaults, unless the user changes them:
- A row of windows in each side wall of the cabin at eye height, and one wide window in the nose wall. The drawn hull gets holes there. BoxMesh cuts holes only in faces across X today, so the nose window needs holes across Z.
- Physics keeps the walls solid: glass blocks a person as a wall does.
- Each pane is a thin transparent box with a slight blue tint. Panes cast no shadow, so sunlight comes in through the holes and draws bright patches in the cabin, which tests the shadows too.
- New captures look out through a pane at Earth, and in from outside at the lamp-lit cabin. Update the slice probes that sunlight in the cabin changes. Measure the cost of the transparent pass.

## Acceptance Criteria

From inside the cabin, a capture through a pane shows Earth tinted by the pane. From outside, a capture shows the lamp-lit cabin through a pane. Every slice test passes, and the cost of the panes is measured.

