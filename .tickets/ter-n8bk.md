---
id: ter-n8bk
status: open
deps: []
links: []
created: 2026-11-12T07:03:49Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# Buffer updates and copies out of the middle of the frame: 0.10 to 0.20 ms

Sweep addendum (/private/tmp/tertius-sweep-20261111/addendum.md) A6 proposal 5. Each vkCmdUpdateBuffer and copy is a blit encoder, and the compute pass after it starts a new encoder. The ray plan's two counter updates can become a dispatch or the plan's own reset; FSR's constants can live in a buffer that the CPU writes; the environment prefilter's image-to-buffer copy can move to the pass's end. The transparent pass's color copy stays.

