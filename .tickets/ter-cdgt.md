---
id: ter-cdgt
status: closed
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


## Notes

**2026-10-21T15:51:43Z**

Built in d62f49b, reviewed and fixed in 6e0376c on top of a merge of main (4c6560f). The review's real defect: the heading followed the person's velocity, so a push that is not a walk turned it (61 degrees under the belt at the ramp's foot in demo-launch, and 0.89 rad in the fetch). The heading now turns toward the walk that the person asks for in the tick, self.Body.Pending.CharacterVelocity, at Priority 10. Full suites green in Debug and Release. The long master is now 22,771 frames and the video 392.5 s. Gait's bob still follows the motion, so a push across a floor bobs it a little, which the plan records.

**2026-10-21T16:19:02Z**

Merged into main on 2026-10-21. Coverage of the changed lines: 97 of 99 lines and 24 of 26 branches. The two uncovered lines are DisembarkState.GetHashCode and the guard in Gait.Handle that returns when the person is gone or has no pose in the frame. The only failures under coverage are the two known allocation tests of ter-odo6.
