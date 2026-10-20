---
id: ter-iq0w
status: in_progress
deps: []
links: []
created: 2026-10-20T14:50:37Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [docs]
---
# GUIDE.md sections 1 to 6 still describe MotionDriver, SetFrameDriver and SetFrameSpin, which the rails epic removed

The review of ter-n44b found stale text in GUIDE.md near lines 70, 168 and 169: 'Today this enum chooses the motion of a frame' about MotionDriver, and pointers to SetFrameDriver and SetFrameSpin. The rails epic removed them: every frame follows its owner, and the owner's components move it (CLAUDE.md, the row on MotionDriver). Rewrite those items against the code on main, with links that point at code that exists.

