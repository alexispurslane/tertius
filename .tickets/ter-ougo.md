---
id: ter-ougo
status: open
deps: [ter-f28b]
links: []
created: 2026-10-12T22:36:29Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-s02r
tags: [physics, frames]
---
# Nest numeric frames for vast distances

Design: docs/plans/numeric-frames.md, section Nesting for vast distances. A numeric frame farther than a threshold from its tree parent's origin gets a resting frame above it, on a grid of 2^20 m so that its position is exact. The resting frame jumps by whole grid steps when the frame below drifts, and the frame below shifts back by the same amount. Reads between nearby entities compose through the lowest common ancestor. Look at the render snapshot's ComputeRelativeTo, which places frames through a shared root.

## Acceptance Criteria

A body that drifts a light-year away from its semantic frame's origin keeps millimeter steps relative to a body next to it, in physics, in reads and in the render snapshot.

