---
id: ter-bpvl
status: closed
deps: [ter-zzyr, ter-3att, ter-cl86, ter-iz58, ter-wjib]
links: []
created: 2026-10-22T00:09:17Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-s9cv
tags: [rendering, long-term]
---
# Lights instead of one sun: every light in SI units, one light list, shadows chosen by geometry

Design: docs/plans/local-light-shadows.md, section "Lights instead of one sun". The user chose the design on 2026-10-21: SI units, one light list for the renderer, and a reach derived from the power. World.SunFrame, SunColor and SunIntensity go away, and a star is an entity with a point light. It waits for automatic exposure (ter-zzyr), because SI units need exposure and pre-exposed HDR values.

