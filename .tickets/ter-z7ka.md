---
id: ter-z7ka
status: open
deps: [ter-irrj]
links: []
created: 2026-10-20T18:45:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-xr2b
tags: [demo]
---
# Live world part 6: the tug and the tow back to the ramp

Part 6 of docs/plans/live-world.md. Build Tug, Cradle, tug-garage, TowPlan and DrivePlan, CradleRise, and the leg case Towed. The tug spawns in demo-surface in this part and waits at its garage until part 7. The tug and the cradle write their end poses relative to the frame that they are in, straight from the plan, so the tow stays exact across the tug's hand-off into the cabin's frame ("The tick of a hand-off"). The shuttle, the tug and the cradle each follow one pure plan with their own end poses, and the tow ends exactly on the park pose. See "The tow". Tests: "Part 6: the tug and the tow".

