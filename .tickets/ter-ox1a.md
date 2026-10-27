---
id: ter-ox1a
status: open
deps: []
links: []
created: 2026-10-27T11:48:52Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, lights, performance]
---
# A light's reach from the last frame's exposure, so the lights that cannot reach the view drop before the lighting pass

Follows ter-p1u7, which the user dropped on 2026-10-27 (the per-pixel share bound was slower than main: 4.19 -> 4.58 ms in lamp-hall-16, 7.44 -> 8.49 ms in demo-exit). The profile of main found that about 3.6 ms of demo-exit's lighting pass is the loop over every lamp at every pixel: a light's reach is the distance at which its irradiance falls below the cutoff, and the cutoff is set for the highest exposure the camera can reach (+10 stops), so a 10 W lamp reaches 1.46 km and touches every pixel.

The user chose (2026-10-27): set the cutoff from the exposure of the last finished frame. At an exposure of 0 stops a lamp's reach shrinks about 32 times, to about 45 m, and the renderer drops the lamps whose reach misses the view before the lighting pass. Still per pixel, no tile or cluster list, no second pass (the user's decisions on the light list, ter-p1u7). When the view darkens, the exposure rises over about a second and the reach grows with it; a light fades to 0 at the edge of its reach, so it does not pop. In a dark scene such as lamp-hall-16 it saves nothing, because the exposure is already high and every lamp shows there.

Design points for the builder to settle by the rules (stop on a fork that changes what the user sees): where the last frame's exposure comes from on the CPU (the exposure lives on the GPU; stored state describes the last finished step), the margin over that exposure, and the fade band at the edge of the reach. Portable Vulkan only.

## Acceptance Criteria

The lighting pass on demo-exit at 1080p, Release, measured on a quiet machine against main, drops by the share of the lamps whose new reach misses the view; lamp-hall-16 does not get slower. No light pops at the edge of its reach when the exposure changes (a capture series over an exposure change shows it). ARCHITECTURE.md names the rule.

