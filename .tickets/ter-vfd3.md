---
id: ter-vfd3
status: open
deps: []
links: [ter-abun, ter-35w5]
created: 2026-11-09T01:47:24Z
type: bug
priority: 3
assignee: Alexis Purslane
---
# GpuTimingTests' empty pass still fails under heavy load, after ter-abun and ter-35w5

Seen again on 2026-11-08 in the rectangle light's full Checked run (ter-e436), at a machine load near 30 on 10 cores. Run alone it passed 4 of 5 times on the branch and 2 of 2 on main. ter-abun and ter-35w5 closed the same failure before, so their fixes do not hold at this load. Find why an empty pass measures more than its limit when the CPU is saturated, for example a late timestamp latch or a preempted submit, and make the test hold at that load or skip it with a stated reason.

