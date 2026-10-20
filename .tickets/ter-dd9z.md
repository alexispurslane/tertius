---
id: ter-dd9z
status: open
deps: [ter-51ee]
links: []
created: 2026-10-20T18:45:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-xr2b
tags: [demo]
---
# Live world part 9: free play at the terminal

Part 9 of docs/plans/live-world.md. Build Seat, PlayerBelt, SeatBelt and Belted in PersonControl, the player, the start live-terminal, the views, and the measurement of real-time cost. Free play runs in Release with the debug checks off. See "Free play". Tests: "Part 9: free play at the terminal".


## Notes

**2026-10-20T20:43:46Z**

From the coverage of ter-tzf6: Passenger.Return outside the seat's frame (Passenger.cs, lines 311 to 314, where the person stops relative to its own frame) has no test. The scripted passenger never leaves the cabin's frame while it returns. PlayerBelt takes the passenger's laws, and a player can float out of the cabin, so this part tests that path.
