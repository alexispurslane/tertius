---
id: ter-vges
status: in_progress
deps: []
links: []
created: 2026-11-03T12:22:22Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, design]
---
# Design: the temporal renderer, docs/plans/temporal-renderer.md

Writes the design doc of epic ter-89xq from the settled decisions in its notes and the research in docs/research/temporal-renderer/, with the order of work as a list of tickets.


## Notes

**2026-11-03T12:30:45Z**

Launched 2026-11-03: workflow run 11bf7522-9923-4941-b1c5-617ca81ac0b3, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (design as the build stage at Opus xhigh, one review, one fix), branch ter-vges-temporal-renderer-design from 987b8d2c2.

**2026-11-03T13:31:51Z**

2026-11-03: the user chose option B of the open question, many bounces: a diffuse ray's hit also brings its own indirect diffuse history of the last frame (bounces = 1). The orchestrator's model, added to the user's question: each bounce passes through the 32-frame diffuse history again, so the later bounces settle with a time constant of 32 / (1 - g) frames, where g is the share of light that the room returns per bounce (64 frames at g = 0.5, 160 at g = 0.8, without the anti-lag). A still of 32 warm-up draws shows about 90 % of the settled light at g = 0.5 and 57 % at g = 0.8. ter-bsxh is closed, so ter-13rm alone closes the gap between the screen's many bounces and the map's one bounce.
