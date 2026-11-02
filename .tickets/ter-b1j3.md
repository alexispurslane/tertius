---
id: ter-b1j3
status: open
deps: []
links: [ter-s6zr]
created: 2026-11-02T18:24:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, environment-map, reflections]
---
# The environment faces draw a lamp's glow dark, so rough reflections from the map miss the lamps that SSR and the reference see

Found by ter-s6zr's probe (run efa3cac1, 2026-11-02). environment_face.frag draws the glow of an entity that holds a light dark (glowSent), so that the map's irradiance does not light the room with the lamp's power a second time. The specular map comes from the same faces, so every reflection that reads the map also shows the lamp dark. SSR's screen rays and ter-f3g2's 64-ray reference (lobenorm, SSR's trace with 64 rays) see the lit lamps. A probe that draws the glow in the faces (glow) moved brake-run's teeth, the antispinward edge and the chute ceiling to 148.7, 126.2 and 95.2, against the reference's 141.2, 120.7 and 96.2; it also changed the diffuse light and the exposure. To decide first: whether the map's specular read must show the glow, given that the sphere light's analytic lobe (ter-gf0t) already gives the lamp's highlight, so that neither the irradiance nor the specular counts the lamp twice. Then the smallest change that gives that, for example the glow kept out of the irradiance convolution only. The probe also named the faces' missing ambient light as a likely cause of the chute-top slide and the floor's lamp cut, which no probe showed cleanly.

