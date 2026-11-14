---
id: t2-zcr4
status: open
deps: []
links: []
created: 2026-11-12T20:56:34Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, performance]
---
# The light loop of an environment face costs 0.25 ms per frame for 16,384 texels: a list of the lamps that reach a face

Found by the frame-time sweep on 2026-11-12 (docs/research/frame-time-sweep-2026-11-11/addendum.md, section A13, the side finding).

The environment faces pay 0.47 ms per frame on the deck, for one face of 128 x 128 texels. Each fragment of a face shades every light (environment_face.frag, directRadiance). A proxy build has faces that shade no light (edits/envnolight.py in the research folder). Its faces take 0.135 ms in place of 0.389 ms. Both numbers are medians of the engine's pass timers in take 12. The sum with the prefilter falls from 0.737 to 0.493 ms. So the light loop costs about 0.25 ms.

A possible exact cut: the batcher gives each drawn face the list of the lamps whose reach meets the face's view. The face's fragments loop over that list only. With a conservative list, the image stays the same. The gain is below 0.25 ms and not sized: on the deck the lamps lie on every side of the eye.

Before a build, count the lamps that reach each of the six faces on the deck. Also make sure that the key-and-fill design of the tile agent (t2-vsqh) does not already change the face's light loop. The proxy's frame span does not compare with main's, because its dark map changes the work of the rays.

