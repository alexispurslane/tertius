---
id: ter-bdt3
status: open
deps: [ter-4xw0]
links: []
created: 2026-11-01T01:23:33Z
type: feature
priority: 4
assignee: Alexis Purslane
tags: [rendering, shadows]
---
# Shadow tiles: keep a holder in the light list while sqrt(2) times its reach meets the view (design B of ter-4xw0)

Trigger: start this ticket only if a scene shows a point lamp's shadows that pop at the view's edge. Until then it waits, by the project's rule to leave out a feature that is not needed yet.

Today (ter-4xw0, design A) the holder margin spans tiers only inside the light list. The batcher drops a light whose own reach misses the view and that no drawn face keeps (SceneBatcher.Reaches), so a holder that the batcher drops loses its shadow tiles at once. A point lamp is in the main view or not, so it gets the margin inside its tier only: a point lamp whose reach slips out of the view hands its tiles over at once.

Design B: the batcher keeps a holder in the light list while sqrt(2) times its reach meets the view, so the margin also spans the main view's edge for point lamps. It couples the batcher to the holders of ShadowTiles (the records of the last submitted frame). See docs/plans/local-light-shadows.md, settled fork 2, and ARCHITECTURE.md, 'Local shadows', the holder margin.

