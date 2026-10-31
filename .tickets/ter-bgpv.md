---
id: ter-bgpv
status: in_progress
deps: []
links: []
created: 2026-10-31T18:44:05Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# The haze pass skips a view in which no opaque pixel can reach the air

Review finding 4 (low) of ter-uu2y, 2026-10-31. Since ter-uu2y, a camera whose chain of frames holds no planet medium takes the planet medium that is densest at its eye. So every view of a world with a hazed planet has an atmosphere record, and the haze pass (HazePass.cs) runs over every opaque pixel, even when the planet is far away or out of the view and T is 1 everywhere. The direct light also pays the tint's exponential. ter-6vhj measured the haze pass at 0.126 ms and the whole look at 0.35 ms of frame span at 1920 x 1080 (ARCHITECTURE.md, 'The atmosphere'). Measure the cost in such a view first. If it matters for the 12 ms target, skip the haze pass, and give the light no tint, when no opaque pixel can reach the air: for example, when the eye's density share is 0 and the sphere of radius R + 40 H lies outside the view frustum. The rule must change no image in which the air is visible.


## Notes

**2026-10-31T19:26:23Z**

2026-10-31: workflow run a7ce6569-5534-40ac-8196-fcef74c2c7b5 (version 869b0b07-3413-4b06-8707-a88a91bf6e91), branch ter-bgpv-haze-skip from 9d5eb53c. Measure first; build the skip only if a shipped view pays more than the timer's noise. The skip must change no pixel, and the tint's sun path decides whether the tint can skip too.
