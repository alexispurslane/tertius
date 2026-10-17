---
id: ter-h78w
status: open
deps: []
links: []
created: 2026-10-17T05:07:23Z
type: bug
priority: 4
assignee: Alexis Purslane
tags: [physics, contacts]
---
# The rebound rule is approximate on Jolt's linear cast path and near its limit in numeric frames

From the fix of the rebound rule (2026-10-17): FrameWorld.KeptRestitution computes Jolt's rebound u = e * (approach - a * dt) at the fastest contact point. On the linear cast path Jolt does not take the forces off its rebound, so the rule removes a little more there, and Jolt measures spin about the center of mass at the hit, which a callback cannot know. In numeric frames, chord velocities may move the decision by about e * g * dt / 2 near the limit (not verified). Each case only moves a bounce that lies near the 4 cm limit.

