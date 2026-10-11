---
id: ter-i1iu
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T14:29:38Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design]
---
# Decide whether the backend's tolerated inputs become bug-only states

The backend logs warnings for inputs that only a bug produces when the World is its only client: a frame that left the tree without FramesDestroyed, a BodyCreate for a dead frame, a reused slot, and others. The Errors channel of round eight can report them.

