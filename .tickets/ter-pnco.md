---
id: ter-pnco
status: open
deps: []
links: []
created: 2026-10-16T13:36:33Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# Cut the cost of Jolt's persisted-contact callback for contacts that wait for their touch

From ter-wu6v: a contact found at Jolt's speculative distance waits as pending, and Jolt's persisted callback detects its later touch. That callback costs about 15% of a tick for 300 awake boxes (1.79 ms against 1.54 ms), all of it in JoltPhysicsSharp's per-call transition, so the backend subscribes only while pending pairs exist. A cheaper way: after the step, run a narrow-phase query for the pending pairs only.

