---
id: ter-9yvr
status: closed
deps: [ter-dhkf]
links: [ter-vja8]
created: 2026-10-17T14:34:17Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-uu1x
tags: [physics, frames]
---
# No numeric frame falls inside a turning frame

Part of the epic on fewer, more reliable numeric frames (change 1). A group inside a frame with the Spin driver, or inside any frame that turns, gets no falling frame. Inside a spinning station the speeds are gameplay speeds, so a falling frame buys no precision, and it is what made the spin-gravity spike fail (a person crossing a station at 40 m/s got a falling frame five ticks before landing and went through the floor).

If this rule lands, ter-vja8 (numeric frames inside turning frames, with the origin on its inertial line) is superseded. Ask the user before closing ter-vja8.


## Notes

**2026-10-17T20:59:04Z**

2026-10-17: closed by the user's rule that planned work gets no stopgaps. ter-vja8 builds falling numeric frames inside turning frames properly, and it now comes before the spinning station scene (ter-n44b).
