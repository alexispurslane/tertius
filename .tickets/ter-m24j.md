---
id: ter-m24j
status: open
deps: []
links: [ter-b1j3]
created: 2026-11-02T19:47:52Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [content, lights, reflections]
---
# A cabin panel lamp shows in a mirror as its light's small disc on a dark panel, because its light's sphere (5 cm) is much smaller than the panel (1.2 m)

Found by ter-b1j3 (2026-11-02). After ter-b1j3, every reflection counts a lamp once, through its light's lobe alone: SSR, the map and the bounce source all read the glow of an entity that holds a light dark. So a lamp whose glowing surface is much larger than its light's sphere shows in a floor below roughness 0.37 as the light's disc on a dark panel. The cabin panels are such lamps: a 1.2 m square panel with a light of radius 5 cm. No demo view has such a floor under a panel today, so nothing in the demo shows it. Options to weigh: a light whose sphere matches the panel's size (content only), or a rectangle light with its own lobe (a renderer feature, which needs a design). Not in epic ter-b2da: the demo does not show it.

