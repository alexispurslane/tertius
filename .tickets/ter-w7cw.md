---
id: ter-w7cw
status: open
deps: []
links: [ter-1w06]
created: 2026-10-17T14:21:29Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, character]
---
# A character in the air under gravity pushes a dynamic body without taking the reaction

Found by the weightless push investigation (ter-1w06). Design B of that ticket turns only a weightless character into a rigid body. A character that does not stand under gravity, for example in a jump or a fall, stays a Jolt CharacterVirtual. When it hits a dynamic body, Jolt's HandleContact gives the body an impulse of up to CharacterMaxStrength x dt per call, but changes nothing in the character's velocity, and WithoutMotionIntoContacts keeps the character's whole velocity against a dynamic body. So the pair gains momentum from nothing: in the investigation, 249.9 N s against a thrust impulse of 208.3 N s after 2 s (weightless case, gap 0.2 m, before the fix). Also, a character and a crate that fall side by side drift apart: the crate fell 0.14 m below the character in 1 s (experiment E11), because Jolt sweeps the character against the crate's pose at the start of the step. Done means: a character in the air that hits a free crate loses the momentum that the crate gains, and a character and a crate that fall together keep their places.

