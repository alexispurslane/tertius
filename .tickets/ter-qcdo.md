---
id: ter-qcdo
status: open
deps: []
links: [ter-1w06]
created: 2026-10-17T15:20:21Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, character, sleep]
---
# A weightless character lowers the sleep threshold of a whole system, also the threshold of the heaps in a ship's gravity

Found in ter-1w06. The gravity pass gives each system the sleep threshold of the weakest effective gravity on its awake dynamic bodies (SleepThresholdFor). A character in its rigid form is a dynamic body, so a weightless person outside the cabin of the orbit scene's ship, but in the ship's system, puts the floor of 3.1 um/s on the whole system, the cabin's crates included. A loose crate out there did the same before. The rigid form needs the low threshold for itself: with Jolt's own 0.03 m/s, a person drifting slower than about 0.03 m/s would fall asleep and stop. Pitfall 6 says that a threshold much below Jolt's default at 1 g keeps a tumbling heap awake for minutes. Measure whether the cabin's heaps stay awake while a person floats outside, and decide whether weightless bodies need a sleep rule of their own (for example AllowSleeping false for the rigid form, or per-body sleep through a contact of the system).

