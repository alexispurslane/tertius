---
id: ter-9lgm
status: open
deps: []
links: []
created: 2026-10-17T02:58:53Z
type: chore
priority: 4
assignee: Alexis Purslane
tags: [tests]
---
# Remove the Restitution: 0 settings in tests that the default now covers

From ter-4kaa: the default restitution of a body is now 0, so about 20 test helpers that set Restitution: 0 (GravityTests, HandOffTests and others) set the default. Remove them, or say in each why the test names the value.

