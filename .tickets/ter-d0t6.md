---
id: ter-d0t6
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T14:29:38Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design]
---
# Spawn and the gravity field accept values whose physics overflows or rounds to zero

A uniform acceleration of 1e38 m/s2 is valid, and a 10 kg crate becomes NaN within 30 ticks with no error. Bound the field so that every force it gives stays finite.

The review of the round eight fixes found two more cases. Spawn tests a rotation in double precision, but physics normalizes it in single precision, so (0,0,0,1e-30) and (0,0,0,1e39) pass and become NaN. A mass above about 3.5e37 kg passes, and gravity times mass overflows float. Refuse each at spawn, with a test.

