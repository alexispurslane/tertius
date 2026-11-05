---
id: ter-12xp
status: open
deps: []
links: []
created: 2026-11-04T22:46:40Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# Single bright pixels on the deck's walls in a still under the screen tracer

A headless still of demo-exit's deck at 1280 x 720 under ScreenTrace.Default, after 32 warm-up draws, shows isolated white pixels over the walls, beside the expected fine grain. The same still under --screen-trace off is smooth. Found during ter-gayt's screenshots (2026-11-04, /private/tmp/gayt-part8/shots, not kept). Find which rays bring them, such as a block ray at a weight near 4 that hits the bright lamp, and whether the slow history's clamp should hold them, and fix the cause.

