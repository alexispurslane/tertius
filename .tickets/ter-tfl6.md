---
id: ter-tfl6
status: closed
deps: []
links: []
created: 2026-10-11T13:52:08Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [physics]
---
# Physics round eight: sleep, spawn refusals and the backend error channel

Workflow wf_ea81abf7-ccb. The user chose to keep sleep under gravity. This round fixes bodies that sleep on slopes and ledges in weak gravity, wakes bodies when their support goes away, and fixes the character re-touch. It also validates orbits, refuses at spawn every body that the backend refuses, and gives the backend a channel to World.Errors.


## Notes

**2026-10-11T14:29:38Z**

Done in 478fd29 to a9e8b23. Suites pass in Debug and Release: Core 61/60, Simulation 1258/1280, Physics 230. The review found the critical problems that ter-qawf fixes.
