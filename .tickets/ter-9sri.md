---
id: ter-9sri
status: in_progress
deps: []
links: []
created: 2026-10-17T03:35:29Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# Removing one body or ghost scans every contact pair of its physics system

From the review of the contact sweep: FrameWorld.ForgetContacts visits every key of touching and held in the system to find one body's pairs. Since ter-ejx9, every ghost removal calls it too, so a ship that flies through 2,000 resting rocks (about 5,000 touching pairs in space) and drops ten ghosts in a step makes 50,000 key checks under the contact gate on the physics thread. Keep an index of pairs per body, so that a removal visits only its own pairs, and measure it.

