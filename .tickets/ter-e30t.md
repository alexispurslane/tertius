---
id: ter-e30t
status: open
deps: []
links: []
created: 2026-10-11T21:18:59Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [simulation, design]
---
# Decide what a Kepler or Spin frame does when its parent is destroyed

Found by the review of ter-uucg. FrameTree.Destroy moves child frames to the grandparent and keeps their poses and velocities. But a Kepler frame's orbit and a Spin frame's spin are given relative to the old parent. At the next tick, ApplyKepler or ApplySpin rewrites the pose from those parameters in the new parent's axes. Example: destroy the orbit scene's earth frame, and earth-surface becomes a child of sun with Earth's world pose (tilted 23.44 degrees, moving at about 29.8 km/s). On the next tick, its rotation jumps by the tilt and its velocity becomes 0, so it hangs at 1 AU. A Kepler moon of a destroyed planet jumps to its orbit round the sun's origin.

Design to decide (two designs, then the user chooses): fold the old parent's pose into the driver's parameters when the frame moves (for a spin, Tilt' = (new parent from root)^-1 * (old parent from root) * Tilt; for an orbit, new elements from the frame's state in the new parent, with the new parent's G·M), or give the frame the Static or Physics driver with its converted state and report the change in World.Errors or an event. Kepler frames already behave this way today, so the decision covers both drivers.

## Acceptance Criteria

Destroying a Kepler or Spin frame's parent leaves the frame where it was and moving as it was, by the chosen rule, and a test covers each driver.

