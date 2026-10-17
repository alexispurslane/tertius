---
id: ter-2cmo
status: open
deps: []
links: []
created: 2026-10-17T00:20:12Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, frames]
---
# A numeric frame made for a spawn takes the spawn's instant velocity, but numeric frames store a chord

From ter-ediu: the World creates a numeric frame for a fast spawn with the spawn's instant velocity, while the frame velocity is a chord, so the bodies in that frame drift relative to it at g dt / 2. Their paths in their semantic frame stay correct, so this costs only an early drain. Give the frame the chord.

