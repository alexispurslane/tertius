---
id: ter-tftg
status: open
deps: []
links: [ter-kugq, ter-1w06]
created: 2026-10-16T12:38:20Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, character]
---
# A walking character slows for about 3 ticks when it touches a light resting crate

The Sonnet reader of surface-yard saw the person's step fall to 0.003 m per tick for about 3 ticks when it walked into the 5 kg top crate, where a 70 kg walker should lose about 0.2 m/s. Probably Jolt's CharacterVirtual treats the resting crate as an obstacle until the push lands. A hitch, not jitter.


## Notes

**2026-10-17T14:08:36Z**

Seen in the ter-1w06 investigation (not the same cause: a walker is OnGround, so Jolt's steep-slope cancel does not run). Jolt's CharacterVirtual moves the character before the physics step and sweeps it against the crate at the crate's start-of-step pose, so a character that touches a crate moves at most as far as the crate moved in the last step. Jolt's push is capped at MaxStrength x dt per HandleContact call, and one update can call HandleContact several times (each MoveShape iteration and WalkStairs makes fresh contacts). Measured, 1.5 m/s walk (0.025 m per tick) into a resting crate: 5 kg crate, steps 0.0050 and 0.0067 m, then the crate leaves at 1.82 m/s, faster than the walker; with CharacterMaxStrength 10000 N it leaves at 6.3 m/s. 20 kg crate: steps 0.0050, 0.0017, 0.0067, 0.0136, 0.0188 m, then 0.0209 m per tick (1.25 m/s) while pushing.
