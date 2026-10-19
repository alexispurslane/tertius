---
id: ter-ybti
status: open
deps: []
links: [ter-6htl]
created: 2026-10-19T20:57:50Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics]
---
# A fast dynamic hull is thrown at a joint of a static compound of planks

Found by ter-m13s (2026-10-19). A dynamic hull that slides fast over the joints of a static compound of planks (a curved launch ramp of 1-degree planks) is stopped and thrown at a joint, with a pitch rate of 7 rad/s. It starts once the hull moves more than 0.75 of its inner radius per step (about 90 m/s for the shuttle), when Jolt's linear cast takes over; the enhanced internal edge removal of ter-fb3w does not prevent it. The demo uses a straight ramp with one joint, crossed at 27 m/s. A curved ramp needs a fix in Physics.

