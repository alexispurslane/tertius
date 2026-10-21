---
id: ter-cdgt
status: in_progress
deps: []
links: []
created: 2026-10-21T14:13:35Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo, video]
---
# Demo walk: the person's drawn body turns to face its way and steps

The user chose this on 2026-10-21, because plumes do not fix the person's glide. Today the person is a box 0.6 x 1.8 x 0.6 m that Walker.Step moves at a constant 3 m/s with MoveCharacter, that turns its path at once at each waypoint, and that never turns to face its way, because only a teleport turns a character (PersonControl.cs remarks). The drawn body turns to face the direction of the walk at a limited rate, and it bobs a little with each step, in demo code only: for example the person's own render row goes invisible and an attached drawn body, whose own component rewrites its AttachOffset each tick, carries the look. The character and its physics do not change. The person stays a gray box.

