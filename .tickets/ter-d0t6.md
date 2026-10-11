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
# GravityField.IsValid accepts finite fields whose force overflows float

A uniform acceleration of 1e38 m/s2 is valid, and a 10 kg crate becomes NaN within 30 ticks with no error. Bound the field so that every force it gives stays finite.

