---
id: ter-6vhj
status: open
deps: [ter-hro7]
links: []
created: 2026-10-25T12:01:26Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, frames]
---
# Atmosphere color per semantic frame: the sky backdrop, and a tint over the view where the atmosphere calls for it

The user asked on 2026-10-25 for an atmosphere color that each semantic frame determines, for Mars-like planets and others in the future. Two uses:
- The sky backdrop. Today the sky is a procedural star field, fixed to the axes of the root frame of the camera's frame tree, black between the stars. Inside an atmosphere, the sky shows the atmosphere's color (blue for Earth, butterscotch for Mars), and the stars fade.
- In some cases, a tint over everything: for example the color of the light through a dusty atmosphere, or haze that grows with distance.

The frame's MediumField already describes an atmosphere's density for drag and lift, so the color can live with it or beside it. The sky color of the hemisphere ambient light (the ticket on per-frame sky and ground colors) can follow from the atmosphere's color.

Design to decide (two designs, then the user chooses):
- Which frame's atmosphere sets the sky: the camera's semantic frame, or the frame whose atmosphere the camera is inside, by the medium's extent.
- How the sky fades from the atmosphere's color to black with altitude.
- Whether the tint is the sun's color after its path through the atmosphere, a haze by distance in the lighting pass, or both, and which cases turn it on.
- How it meets exposure and the tone curve of the rendering epic.
- What the render snapshot carries for it.

## Acceptance Criteria

A Mars-like test frame shows a colored sky behind its ground, with the stars faded, and the space above it stays black. The tint shows where the design turns it on. The look is reviewed in headless screenshots, and ARCHITECTURE.md describes the rule.

