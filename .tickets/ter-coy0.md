---
id: ter-coy0
status: open
deps: []
links: []
created: 2026-11-02T20:04:22Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, impostors]
---
# A lamp drawn as an impostor billboard keeps its glow in the screen's reflections and the bounce, so they count it twice

Found by the review of ter-b1j3 (2026-11-02). impostor.frag writes the light mark 0 (outShading = 0), because the atlas stores the captured light and the glow as one sum, and the pyramid cannot take the glow out of it. So a lamp entity that the impostor manager draws as a billboard (under 200 px) shows in a glossy floor through its light's lobe and through SSR's ray, and its glow bounces as well (screen-space-illumination.md, decision on the light mark). The environment faces draw impostor objects as geometry, so the map counts such a lamp once. Nobody measured how much a distant lamp adds. To decide: whether the case needs a fix (for example an impostor capture that leaves out the glow of an entity that holds a light, so that the billboard's light and the light's lobe add up to the lamp once), or a recorded exception, with a measurement of a distant lamp's double count in a demo view.

