---
id: ter-myew
status: open
deps: []
links: [ter-1w06]
created: 2026-10-17T15:20:07Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, character]
---
# A character's rigid form takes a push of 7 mm/s from the airlock sill's rounded edge as it leaves the cabin's gravity

Found in ter-1w06. In the orbit scene the cabin's gravity ends at x = -9.5 m, the inner edge of the hatch's sill. A person who walks out at 3 m/s is still 2.4 mm above the sill after its step up when it crosses that line, so it turns rigid there. In its first step the rigid capsule passes 3 mm over the sill box's inner edge, which Jolt rounds by the box's convex radius of 0.05 m. Jolt's speculative contact there, plus friction, changes its velocity by (+0.0046, +0.0078) m/s, so it drifts up at 7 mm/s outside and comes back from a spacewalk about 0.1 m higher (reproduced in a physics test with a compound of a floor box and a sill box, a gravity region that ends at the sill's inner edge, and a walk at 3 m/s from x = -7.9939). The slice tests AirlockSliceTests and SpacewalkSliceTests now expect the drift. Decide whether that is fine, or whether the gravity region should end farther out, or the switch should wait until a walking character has settled on its ground.

