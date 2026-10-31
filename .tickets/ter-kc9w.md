---
id: ter-kc9w
status: open
deps: []
links: []
created: 2026-10-31T20:03:27Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# The exposure pass costs about 0.1 ms more in frames that run the haze pass


## Notes

**2026-10-31T20:03:27Z**

Found by ter-bgpv's measurement (2026-10-31): in mars-air, paired traces that removed the atmosphere record on every other frame (Metal System Trace without counters, 200 frames at 1920 x 1080, Release, GPU lock) show the exposure pass costing +0.115 ms in the frames that run the haze pass. ter-6vhj measured the same kind of cost (ARCHITECTURE.md, 'The atmosphere' cost table: exposure 0.126 ms against 0.069 ms, 'Not explained: the meter's work does not read the look'). The meter reads the HDR target after the haze, so a candidate is a dependency or a barrier that the haze pass adds before the exposure pass, or a cache effect of the haze's full-screen write. Find the cause with the trace, and remove the cost if it is not inherent. Measure with frame-alternating paired traces: on a loaded machine, separate runs drift by about 13 % in every pass.
