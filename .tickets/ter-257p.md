---
id: ter-257p
status: open
deps: []
links: []
created: 2026-10-12T11:53:27Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [simulation]
---
# A spawn whose parent or frame dies in the same sync point is reported as a bug

Found in ter-upyg's review fixes. An attached spawn now takes its parent's frame at the sync point, so a hand-off of the parent in the same tick no longer fails it. Two races of the same kind remain, and each still adds an error to World.Errors, which is meant for states that only a bug can produce:
1. An attached spawn whose parent dies in the same sync point, for example a person destroyed in the tick in which it takes a crate.
2. An unattached spawn into self.Frame when that frame dies in the same sync point, for example the crate that Carry throws.
A component cannot know at spawn time that the parent or the frame will die at the sync point, so neither case is a bug.

Design to decide: drop such a spawn silently or with a log line, or place it in the frame where the parent's or the frame's other entities went.

## Acceptance Criteria

A spawn that loses its parent or its frame in the same sync point records no error, and a test locks in what happens to it instead.

