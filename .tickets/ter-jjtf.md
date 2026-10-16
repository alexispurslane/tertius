---
id: ter-jjtf
status: closed
deps: [ter-1j1e]
links: []
created: 2026-10-11T13:52:10Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-se84
tags: [tests]
---
# Port the Simulation tests from the physics mock and FakePhysics to the World on Jolt

One test file per Sonnet agent. Move DeterminismTests onto Jolt with more than one gameplay worker.


## Notes

**2026-10-16T13:45:02Z**

Closed too early: the first port moved only 3 Simulation files. About 100 tests in 19 more files still used the physics mock under other names, and ter-tw27 ported them before it deleted the mock.
