---
id: ter-p349
status: open
deps: [ter-1r63]
links: []
created: 2026-11-02T01:38:36Z
type: feature
priority: 4
assignee: Alexis Purslane
parent: ter-7gtc
tags: [rendering, atmosphere]
---
# Volumetric light: light shafts and haze with shadows, inside stations and in atmospheres

The user's request of 2026-11-01, far future, after the asset pipeline and after specular anti-aliasing. Many games of 2016 to 2019 light a participating medium in froxels (Wronski 2014; Hillaire 2015, Frostbite's unified volumetrics) so that lamps and the sun cast shafts through haze. Those methods hide their noise with temporal reprojection, which decision 8 of docs/plans/rendering-performance.md forbids, so the design must stay within one frame: for example a fixed jitter with a spatial filter, or analytic integration along the view ray. Vacuum holds no medium, so the feature matters inside stations and in planets' air; it starts from the engine's existing haze (ARCHITECTURE.md, 'The atmosphere', throughAir) and the shadow maps. Design first, two designs for the user, with cost targets from the performance epic.

