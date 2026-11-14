---
id: tz-frma
status: open
deps: []
links: []
created: 2026-11-14T15:23:37Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, performance]
---
# A list of the lamps that reach an environment face: count them on the deck first (below 0.2 ms)

Found by the frame-time sweep (docs/research/frame-time-sweep-2026-11-11/addendum.md, section A14). It was the first idea of ticket t2-zcr4, which built another cut.

Since t2-zcr4, a face of an environment map lights each texel once. Its light pass loops over every light that the face's map keeps. On the deck, the root map keeps 1 of the frame's 22 lights. The deck's own map keeps all 22, and all 22 cast shadows. The loop over a real surface record costs about 0.2 ms of the pass's 0.35 ms per frame on the M1 Max.

A possible exact cut: the batcher gives each drawn face the list of the lamps whose reach meets the face's view. The light pass loops over that list only. With a conservative list, the image stays the same.

The gain is below 0.2 ms and not sized. On the deck, the lamps lie on every side of the eye.

The first step comes before any build. Count the lamps whose reach meets each of the six faces of the deck's map. If most faces keep most lamps, close the ticket with the count.

Also make sure that the light list per screen tile of the tile agent (t2-mlvy, t2-vsqh) does not already change the face's light loop.

