---
id: ter-kugq
status: closed
deps: []
links: [ter-1w06, ter-tftg, ter-jwzd, ter-dhkf, ter-uu1x]
created: 2026-10-17T13:17:17Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [physics, frames]
---
# Measure how physics precision degrades with a common speed

Asked by the user on 2026-10-17, to choose the speed thresholds of numeric frames from data. Jolt keeps each body's velocity as a float relative to its system's origin, so a group's common speed sits inside every member's float. Measure, with the real World and Jolt and with numeric frames turned off, how each kind of error grows with the common speed, from 0 m/s to 300 km/s: the drift of a thrusting character, the drift of a ballistic body in a field, the relative drift of two free bodies, the creep and jitter of a resting stack, the outcome of a slow collision, the false hits of a character's sweep against bodies that move with it, and the false hits of linear casts between bodies that move together. Separate the effect of distance from the origin from the effect of speed. Report the measured errors as ranges per speed, with no assumed tolerance: the user decides with Claude what accuracy a game engine needs.


## Notes

**2026-10-17T14:34:18Z**

Merged 2026-10-17 (588e07a, c0e317a, 99157a3). Results discussed with the user, who kept 50 m/s for groups that thrust and chose about 1 km/s for groups of free bodies. The character half of the pushed-contact run was moot because of ter-1w06. Follow-up: epic ter-uu1x.
