---
id: ter-a24f
status: in_progress
deps: [ter-b3h5, ter-ptft, ter-jxv9]
links: [ter-jxv9]
created: 2026-10-11T20:51:00Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, long-term]
---
# Add screen-space global illumination (SSGI): one bounce of light from what is on screen

A long-term render feature the user has always wanted. Today a surface gets light only straight from the sun, from local lights and from the flat hemisphere ambient. No light bounces: a sunlit floor does not light the underside of a table, and a red wall does not tint the floor beside it red. SSGI gathers one bounce of indirect light from the lit image on screen: for each pixel, it traces short rays over the depth buffer and takes the color of what they hit.

What the renderer has to build on: the deferred G-buffer (albedo, normal, specular and gloss, emission, Euclidean distance in R32F) and the lit HDR image. The ambient term in lighting.glsl is where indirect light enters today.

Design to decide (two designs, then the user chooses): the method (for example screen-space ray marching over a hierarchical depth buffer, or a horizon-based extension of GTAO that also returns bounced color); which frame's lit image it samples (the previous frame, reprojected, or a separate pass); denoising and temporal accumulation, and how the deterministic switch turns them off; a fallback for light that comes from off screen (the hemisphere ambient, or later a probe or sky term); how it works across the three depth ranges; and its cost at 1080p.

It builds on the same screen-space machinery as SSAO, so it follows that ticket. Per the project rules: a RendererOptions switch, measured cost, deterministic in tests, references reviewed by eye.

## Acceptance Criteria

With SSGI on, a sunlit surface lights nearby surfaces in shadow with its own color, for example a lit floor brightens the underside of an object above it. It fades gracefully where the source is off screen, it is stable over time, it is switchable, and its cost is measured.


## Notes

**2026-10-25T19:06:16Z**

The user's design choices, 2026-10-25, from two designs in the chat: (1) gather the bounce inside the crease shadowing's slices with a mask of 32 sectors per slice (a visibility bitmask), one estimate of the light around each point, so that the light of a surface that blocks a sector replaces the sky light it blocks, over choosing separate Hi-Z rays through the reflections' depth pyramid; (2) this frame's direct light bounces, one bounce, so the lighting pass splits into a direct pass and an ambient pass, over the last frame's image reprojected; (3) blur only, nothing carries over between frames, over a running average. The design doc is ter-zi4i.

**2026-10-25T19:09:47Z**

2026-10-25, later: the user asked whether design A can work at a distance, and chose a hybrid, 'near and far': sector masks inside the crease radius, and beyond it rays through the reflections' depth pyramid, only in the directions that the near sectors leave open. Each direction is blocked nearby, blocked far away, or open to the sky: one estimate. This frame's direct light still bounces, and blur only. The bounce light's build now waits for the reflections' depth pyramid and trace (ter-jxv9).
