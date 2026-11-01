---
id: ter-0saa
status: open
deps: [ter-ww0h, ter-b2da]
links: []
created: 2026-11-01T18:38:04Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, direct-light]
---
# Direct light: drop a shadowed lamp for each 16 x 16 tile that its shadow map proves blocked (design B of ter-ww0h)

ter-ww0h's measurement (2026-11-01, review pending on its branch c2c39308): 40.0 % of the (16 x 16 tile, shadowed lamp) pairs with a tested pixel are dark on demo-exit, and a per-tile shadow test could save up to 0.99 ms per frame (1.23 ms with the tiles that no pixel reaches). The direct light pass needs 1.54 to 1.59 ms less to reach its share of 3.80 ms. Design A (rooms and doorways on the CPU) bounds at 0.39 ms and needs room data that no scene has. The choice between A and B is the user's, after ter-ww0h's review; the builder recommends B. The test must be conservative: it drops a lamp for a tile only when the shadow map proves every pixel of the tile blocked, so the image stays the same.

## Acceptance Criteria

The image is unchanged under the share tickets' image rule. The direct light pass's saving is measured on demo-exit with traces of every frame, against ter-ww0h's bound. A test pins that a tile with one lit pixel keeps its lamp.

