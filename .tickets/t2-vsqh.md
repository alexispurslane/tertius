---
id: t2-vsqh
status: open
deps: []
links: []
created: 2026-11-12T06:39:26Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, performance, lights]
---
# Key and fill: a corner pass with one fragment per corner and group of lamps, tiles of 16 pixels and no ring walk

Waits for the user's decision on fork 5 of docs/plans/light-pairs.md: whether key and fill goes on. Version 2 of key and fill (branch t2-mlvy-tile-light-budget) keeps the image and saves 0.70 ms of the deck's direct pass and 0.89 ms of the hall's, but its corner and tile pass costs 0.61 ms and 1.04 ms, so it does not win. Cost-only builds (docs/research/light-pairs/measurements.md, variants/density.py and parts.py) show that the pass's time does not follow its count of corners, that the same work in shorter fragments runs in half the time, and that fewer corners and shorter fragments add up: tiles of 16 pixels with 4 x 4 copies of the corner grid, each fragment ranking a sixteenth of the lamps, run in 0.265 ms in the hall and 0.252 ms on the deck without the ring walk. The ring walk (nearestLitPixel in key_fill_corners.frag) costs 0.14 to 0.34 ms in the hall at tiles of 8 pixels and about 1.6 ms at 16. The work: a real corner pass of that shape, with a tile pass that merges the groups' candidates into the same keys as one loop over every lamp gives; a cheap replacement for the ring walk; and the image check of tiles of 16 pixels under motion. The expected gain is about 0.45 ms on the deck and 0.6 ms in the hall, of a direct pass of 4.1 ms.

## Acceptance Criteria

The keys of a corner equal those of version 2's single loop on the test scenes. The deck's and the hall's traces show the corner and tile pass and the direct pass together under every light pair's direct pass by at least 0.4 ms. The stills and a film of the deck, the hall and the cabin stay within version 2's image measures. If the traces do not show the gain, the ticket closes with the measurement and key and fill stays out of main.


## Notes

**2026-11-12T06:59:51Z**

The commit report of cb64ce6e3 names code of the prototype to improve before any merge: VulkanRenderer.cs:139 is 101 lines long (limit 60); ScriptCommand.RendererOverrides.ApplyTo has CC 16; DescriptorLayouts.cs:267-276 deconstructs five or more values; KeyFillImages.cs:122 is a pass-through method; LightPairSetting.cs:29 is unused code; KeyFillPass.cs:68-88 and 94-126 are statement walls. The prototype also has no coverage run, no mutation testing and no ARCHITECTURE.md text. They wait with this ticket, because the prototype does not merge as built.
