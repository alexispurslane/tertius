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

