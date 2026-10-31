---
id: ter-ox1a
status: in_progress
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


## Notes

**2026-10-31T14:22:50Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 88962f00-4bb7-41be-89dd-bb3951a51fd8), run 8885be90-f0e0-442f-aa07-43c94ccb9dcf, branch ter-ox1a-light-reach-exposure from cd8b233c.

**2026-10-31T16:12:09Z**

2026-10-31: the first run (8885be90, old script version 88962f00) built the ticket (HEAD 858c84f9, both suites passed) but ended with output_schema. Continuation run 43735db1-af68-4151-a76d-9d28243e9162 (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) verifies the work and runs the review and fix stages on the same worktree and branch.

**2026-10-31T16:26:44Z**

2026-10-31: continuation run 43735db1 verified the work (no change) and its review stopped at fulfillment 'unverified': the demo-exit speed-up is about a third of the share of lamps cut, unexplained, and the lamp counts do not say whether they count map-only lights. Findings: the cut test checks only the exposure (medium), the acceptance gap (medium), the step store keyed by slot (low), 0.1 stops should be about 0.17 (low). Continuation run 95b02844-94b8-44ad-8baa-ed857f22e36d (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) fixes them, measures the pass's fixed cost and the two light counts, adds the invariant check, and checks the shadow tile rank.

**2026-10-31T17:13:55Z**

2026-10-31: fork settled by the user. The user chose to build the shadow tile hysteresis (ter-4xw0). The orchestrator found that the fork's example comes from the sphere test of a spot lamp's main-view flag and the direction-blind eye irradiance, filed as ter-19fm, and the hysteresis follows it. By the user's rule 'quick fix first', ter-ox1a keeps the pure ranking and merges first; ARCHITECTURE.md names the limit until the two tickets land. Fixer run 95b02844 resolved the four review findings in 22935484 and measured the deck: direct pass base 6.21/6.12 ms, branch 5.41/5.41 ms; without local lights 0.49 ms on both; loop 22 -> 15.12 lights (14.25 in the main view); the cut lamps are all far lamps without shadows, so the lamps' cost drops 13 % for 31 % of the loop (load 2.3 to 6, GPU lock, alternated). The orchestrator accepts these as the quiet repeat.

**2026-10-31T17:14:25Z**

2026-10-31: continuation run 600916c9-75eb-4d98-b153-ba1e7721a9d5 (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) adds the shadow tile limit to ARCHITECTURE.md and runs the review and fix stages on 22935484.
