---
id: ter-mpm6
status: closed
deps: []
links: [ter-s02r]
created: 2026-10-13T01:26:00Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# Keep a list of ghosts per physics system, so that a shift does not scan every record

From the review of ter-s02r, finding 9. ApplyShifts (src/Tertius.Physics/JoltPhysicsBackend.Shifts.cs) now moves ghosts with DontActivate instead of removing them, but it still scans every record to find the ghosts of the shifted system.

