---
id: ter-n8bk
status: closed
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


## Notes

**2026-11-12T20:01:22Z**

Measured with a proxy build on 2026-11-12 (take 11, Mac, as a window, main c87eac88f). The proxy records the two header resets of the ray plan's lists at the frame's start, in the blit encoder of the exposure's readback copy. A frame then has 65 encoders in place of 68, and its idle time falls from 1.27 to 1.29 ms to 1.22 ms. The frame span is 19.882 ms against 19.881, 19.917 and 19.941 ms of main: a gain of 0.00 to 0.06 ms, inside the spread of main's traces. FSR's constants and the environment prefilter's copy are smaller parts. The estimate of 0.10 to 0.20 ms does not hold, so this ticket is not worth a build. The record is in docs/research/frame-time-sweep-2026-11-11/addendum.md, section A12, and the proxy is edits/hoist.py there.
