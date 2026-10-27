---
id: ter-9sri
status: closed
deps: []
links: []
created: 2026-10-17T03:35:29Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# Removing one body or ghost scans every contact pair of its physics system

From the review of the contact sweep: FrameWorld.ForgetContacts visits every key of touching and held in the system to find one body's pairs. Since ter-ejx9, every ghost removal calls it too, so a ship that flies through 2,000 resting rocks (about 5,000 touching pairs in space) and drops ten ghosts in a step makes 50,000 key checks under the contact gate on the physics thread. Keep an index of pairs per body, so that a removal visits only its own pairs, and measure it.


## Notes

**2026-10-27T13:07:42Z**

2026-10-27: merged (e91643ab); full Checked on the merged main: 9,084 total, 0 failed. Coverage of the changed lines 218/223 (97.8%), branches 123/136: the misses are the bug-only report in FrameWorld.Unindex and FrameWorldOf, an accessor for benchmarks. Benchmark (2,000 cubes, 5,910 pairs, forget the 5 pairs of one cube): 16.40 us before, 0.342 us after, 0 B allocated.
