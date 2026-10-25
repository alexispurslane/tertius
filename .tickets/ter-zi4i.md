---
id: ter-zi4i
status: in_progress
deps: []
links: []
created: 2026-10-25T19:06:16Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, design]
---
# Bounce light: the design doc

The design doc of ter-a24f, docs/plans/screen-space-illumination.md, on branch ter-a24f-ssgi. The user chose design A in full on 2026-10-25: the bounce is gathered inside the crease shadowing's slices with a mask of sectors (visibility bitmask, Therrien, Levesque and Gilet 2023), one estimate of the light around each point; this frame's direct light bounces, so the lighting pass splits into a direct pass and an ambient pass; blur only, nothing carries over between frames. The doc also settles which of XeGTAO's tuned constants (ter-sdks) carry into sector masks.

