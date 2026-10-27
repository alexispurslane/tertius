---
id: ter-u75o
status: open
deps: []
links: []
created: 2026-10-27T09:39:05Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, frames]
---
# A numeric home's damping acts on the velocity in the home, not in the semantic frame

Found by the review of ter-unpc (2026-10-27). Jolt damps a dynamic body's velocity in its Jolt system. In a numeric home that is the velocity relative to the home, so a damped body relaxes toward the home's velocity in its semantic frame, where without numeric frames it relaxes toward rest there. The difference is an extra acceleration of the body's linear damping rate times the home's velocity in the semantic frame. A box with the default damping of 0.05/s that physics stops in a falling frame at 0.5 m/s gains 0.025 m/s², about 4 mm/s and 0.35 mm in 10 steps. In a home at 7.67 km/s it gains about 6.4 m/s in its stop step, and keeps it after the numeric update moves it out. StoppedBodyInNumericHomeTests pins the limit (ADampedDynamicBodyThatStopsInAHomeThatFallsWithGravityDriftsTowardTheHomesVelocity: 0.71 mm/s and 63 µm in 10 steps in a home at 0.093 m/s). One possible fix: give each damped body in a numeric home the extra acceleration of its damping rate times the home's velocity, so that the damping acts on the semantic velocity. Check first what that does to a damped crate that moves with an orbiting group: without numeric frames it brakes against the planet's frame at its damping rate times 7.67 km/s, and the user may want the home's behavior there. The velocity shadow of turning frames expects Jolt's damping factor and must take the same change. Then name the change in docs/plans/numeric-frames.md, Limits, and in ARCHITECTURE.md.

