---
id: ter-yrfa
status: open
deps: [ter-1bnq]
links: []
created: 2026-10-19T20:57:50Z
type: feature
priority: 2
assignee: Alexis Purslane
tags: [physics, characters, demo]
---
# A weightless character turns its own up by an intent

The user chose this on 2026-10-19 for the Blue Danube demo (ter-m13s): under 3 g of thrust a belted character's up turns against the cabin's gravity, so it tips toward the nose, and in orbit it keeps that up, because a character in free fall keeps its last up. A weightless character should be able to turn its own up, slowly, by an intent, as a person in free fall turns by moving. Design it inside the up rules of docs/plans/spin-gravity.md (fork F5: up follows gravity in the air in every frame; weightless means the effective gravity is below the threshold) and of ter-1b21: the analogue is a character intent (CharacterVelocity, JumpSpeed) written in tick k and applied by step k+1, with a bounded turn rate, ignored while the character has weight. Then use it in the demo launch so that the person floats upright in orbit.

