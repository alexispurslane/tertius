---
id: ter-tfl6
status: in_progress
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

