---
id: ter-hcwx
status: open
deps: []
links: []
created: 2026-10-17T00:20:12Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, queries]
---
# A ray from a parent frame misses a child frame's bodies that lie just outside the child's bubble

From ter-juuk: each system gets only its part of the ray: a child frame the part inside its bubble, the parent the part after the ray leaves the bubble. A child frame's body that lies just outside the bubble, within the hand-off margin, is in the child's system but on the parent's part of the ray, so no cast finds it.

