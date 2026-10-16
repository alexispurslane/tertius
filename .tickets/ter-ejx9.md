---
id: ter-ejx9
status: open
deps: []
links: []
created: 2026-10-16T13:36:33Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, contacts]
---
# A contact across a bubble hand-off ends and starts again when the two bodies do not move together

From ter-wu6v: a contact goes on across a hand-off only if the other entity's body moves into the same system, because contacts with ghosts are not reported. So a crate that enters a frame ahead of a crate that it touches gets Started, Ended, Started. A body that moves into a falling numeric frame away from an anchor that it rests on ends that contact too. Decide whether ghost contacts should carry such a pair.

