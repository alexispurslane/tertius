---
id: ter-qawf
status: closed
deps: [ter-tfl6]
links: []
created: 2026-10-11T13:52:08Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [physics]
---
# Fix the critical problems that the round eight review finds

Read the verdicts of the round eight review. Fix the problems that the vertical slice can hit, test-first. Record every other problem as a ticket under the design review epic.


## Notes

**2026-10-11T15:13:55Z**

Fixed in b3f60a7, 607e854, f0cc48c and 03e1769. The review found nothing critical. Its edge cases went into ter-d0t6. Suites pass in Debug and Release: Core 63/62, Simulation 1258/1280, Physics 257.
