---
id: ter-onkj
status: open
deps: []
links: [ter-qdn8]
created: 2026-11-25T20:09:29Z
type: bug
priority: 2
assignee: Alexis Purslane
---
# Honor the film frame clock when editing a 30 fps master at 60 fps

BlueDanubeEdit can duplicate one frame when a master with 30 fps metadata supplies one image per 60 fps film frame. A 189-frame fixture plus the 1,020 title and credit frames produced 1,210 frames. Passing -r 60 before the master input gave the expected 1,209. The second cut used that input clock for its local edit.

## Acceptance Criteria

Exercise the normal edit entry point with a stamped 30 fps master. Preserve each source image in order exactly once at the film frame rate, align its readout, and verify the title and credit lengths in both video and audio.
