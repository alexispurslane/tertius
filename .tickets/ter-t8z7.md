---
id: ter-t8z7
status: open
deps: [ter-nmbm]
links: []
created: 2026-11-03T19:20:16Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# ReSTIR's spatial reuse: a visibility-aware combine for thin shadows

ReSTIR DI's spatial reuse combines 2 neighbors with Bitterli's biased weights 1 / M and no visibility test of the neighbor's light at this pixel (docs/plans/temporal-renderer.md, section 7, "The bias", item 1). On demo-exit's deck at frame 120 the accumulated direct light measured a bias of -0.107 against EveryLight (ter-nmbm, Release, 1280 x 720), and the thin shadows of the glasshouse's frame on the floor lose most of their contrast. With SpatialNeighbors = 0 the deck still measured -0.006. lamp-hall-64 measured -0.022 and the cabin of cabin-light -0.004. The hall's still also loses most of its pillar shadows on the floor, and its floor shows grain (screenshots of ter-nmbm, /private/tmp/nmbm-shots/base-lamp-hall-64-still/hall.png against after-lamp-hall-64-still/hall.png, 2026-11-03). Option to design: Bitterli's unbiased combine, 1 / Z over the reservoirs whose surface sees the chosen light, with one shadow lookup of the chosen light per neighbor, and the neighbor's candidate weighted with its visibility at this pixel. Measure its cost against the budget of light candidates + direct light (0.6 to 1.3 ms) and its bias on the three views (DirectLightBiasMeasurement).

