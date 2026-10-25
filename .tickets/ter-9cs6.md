---
id: ter-9cs6
status: open
deps: [ter-imi5]
links: []
created: 2026-10-19T02:07:24Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [physics, frames]
---
# Ghosts of interiors and of static bodies that stick out, with the interior's contact carry

Follow-up of ter-imi5 (user's answer to fork F3, 2026-10-18). The pure model gives a ghost to every static body that a body of another system can touch. ter-imi5 builds only the child ghosts of static plain bodies. This ticket adds the other two cases from docs/plans/complete-ghosts.md: an interior gets child ghosts in the frames inside its owner's frame (rooms), and a static body that sticks out of its own bubble (a shuttle's ramp) gets stick-out ghosts with the Ghost role. It also builds the carry of contacts between an interior and its ghosts across a hand-off (the second half of ter-3mwi), so a crate that moves from a room into the ship does not hear CollisionEnded and Collision again from the walls. GhostBody records whether it copies the hull or the interior.


## A probe test in the stash (2026-10-24)

An old workflow worktree held an uncommitted test, NumericFrameInteriorWallTests.cs: a crate thrown at 50 m/s or more at a wall of a ship's interior must stop at the wall with numeric frames, as it does without them. The worktree was removed, and the file is kept in the git stash. Run `git stash list` and find the entry that names this ticket, then run `git stash show -p --include-untracked stash@{N}` to read it.
