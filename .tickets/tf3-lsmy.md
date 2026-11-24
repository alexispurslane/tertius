---
id: tf3-lsmy
status: open
deps: []
links: []
created: 2026-11-24T18:02:56Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering]
---
# The terminal of demo-surface shows white grain on its ceiling and left wall in the headless capture, on main as on ter-fe63's branch

Seen in ter-fe63's normal-entry check, 2026-11-24: scripts/demo-surface.json headless at 1280 x 720, capture 'terminal' (284 ticks after the switch to terminal-camera), automatic exposure.

- Main 56feef7d5: fine white grain over the ceiling, around the ceiling panel near the sunlit door, and over the left wall.
- ter-fe63's branch (945f36790) and its merge with main 6ea0b34f8 (c8ea4ce41): the left wall's grain is mostly gone, and the ceiling keeps about main's grain.

So the ceiling's grain does not come from the reflection share. The sun through the door lights a small floor patch, and the ceiling's light comes from it, so it may be the diffuse rays' rare hits on that patch, or the history length after the cut.

Do: find which signal holds the grain (TERTIUS debug views of diffuse and specular), whether it settles with more draws, and which ticket owns it (the survey ter-6k3k, or the young histories ter-4iic).

