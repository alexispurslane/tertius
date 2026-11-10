---
id: tb-8zew
status: in_progress
deps: []
links: [tb-4g6h]
created: 2026-11-10T00:34:37Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-icn3
---
# Keep the light-age record's last light at the pixel's surface point, so a walk past still lamps reports no light change

Split from ter-wkvw (step 4 of /private/tmp/tertius-report-20261109/proposal.md). The light-change age is, for each pixel, the number of draws since its direct light last changed. Under motion over a steep light, the record's own-band rule kept a stale light: inside the 3 x 3 range it took a light only near the nearest tap's own last light, which belongs to a point up to a pixel away. The pixel's light left that band every draw, the record kept the stale light, and the drift left the range, so the record reported false changes. In the cabin walk, false ages reached 16.4 % of the diffuse and 10.8 % of the specular samples, and main's specular history reads the same record (/private/tmp/tertius-diffuse-age-20261109/note2.md). Build: cherry-pick 3c6348a95 alone from ter-wkvw-diffuse-light-age, without the diffuse history cut. Acceptance: a test of a walk over a still lamp's steep light that is red on main and green on the branch; the walk's false light changes, mean light and settle count measured on main and on the branch.

## Acceptance Criteria

The walk test is red on main and green on the branch. Every still-frame and real-change light-age test passes. Measured on main and branch: walk share of false light changes, walk mean light, cabin-walk settle count.


## Notes

**2026-11-10T07:57:44Z**

Measurements, 2026-11-10, main fc1c51476 against the branch (M1 Max, headless, the cabin walk of /private/tmp/tertius-diffuse-clamp-20261108/s-walk.json: 60 still draws, 120 walking, 100 standing).

| Measure | Main | Branch |
|---|---:|---|
| Pixels that commit a light change per draw while walking, mean (max) | 0.345 % (1.577 %) | 0.109 % (0.438 %) |
| Pending pixels while walking, mean | 7.05 % | 0.68 % |
| Diffuse rays with an age code while walking, mean | 15.5 % | 0.22 % |
| Specular samples with an age code while walking, mean | 11.0 % | 0.19 % |
| Shown mean light, branch / main, while walking (min, max) | 1 | 0.9973 (0.9933, 1.0014) |
| Same, standing after the walk | 1 | 1.0032 (1.0014, 1.0047) |
| Survey walk part, cabin-walk: draws until the whole image settles | 94 | 94 |
| Survey walk part, cabin-walk: slowest region | 200 (not settled) | 200 (not settled) |

The first four rows come from an evidence-map probe (note2.md's probe C, adapted to main's resolve) at a fixed exposure of 3.49 stops. The mean light comes from plain Release builds with compare-g.py and compare-h.py of /private/tmp/tertius-diffuse-age-20261109/scripts. The settle counts come from tools/renderer-survey.py (step 1's copy) run --parts walk --only cabin-walk. The region counts of main and the branch are equal but for two regions (85 -> 87, 63 -> 64), so the repair does not explain the walk's slow settling. Scripts and logs: /private/tmp/tb8zew.

Red-green: AWalkOverTheSteepLightOfAStillLampCommitsNoLightChange counts 4,791 commits on main's shader and 0 on the branch. AWalkTowardADimmingEmitterStillCommitsItsChange gives the same ages on both shaders (1,262 interior pixels at age 2, 102 never changed), so the footprint band hides no change that main's band saw; the 102 misses are ticket tb-4g6h. Full Checked run on f5f57d9dd: 10,713 tests, 0 failed.

Mutation (tools/mutate.py, the changed lines of screen_trace_plan.comp; S01 to S08 on f5f57d9dd, S02 to S07 again on 82a65c969):

| id | place | change | result | killed by |
|---|---|---|---|---|
| S01 | screen_trace_plan.comp:117 | band around the mean only | killed | AWalkOverTheSteepLightOfAStillLampCommitsNoLightChange |
| S02 | screen_trace_plan.comp:117 | keeps the nearest tap light | survived | |
| S03 | screen_trace_plan.comp:123 | pending keeps the nearest tap light | survived | |
| S04 | screen_trace_plan.comp:103 | footprint takes every tap | survived | |
| S05 | screen_trace_plan.comp:96 | reads taps of weight 0 | survived | |
| S06 | screen_trace_plan.comp:99 | takes negative records | survived | |
| S07 | screen_trace_plan.comp:109 | no division by the weights | killed | AWalkTowardADimmingEmitterStillCommitsItsChange |
| S08 | screen_trace_plan.comp:22 | footprint weight 1/4 | killed | AWalkOverTheSteepLightOfAStillLampCommitsNoLightChange |

Answers:
- S02, judgment call: a light inside the 3 x 3 range but outside the footprint band is rare under motion once the band spans the footprint, and the kept light then serves only as a range end for one draw; neither walk test sees a difference.
- S03, judgment call: a pending move lasts at most 3 draws, its kept light serves only as a range end for the neighbors, and the commit takes the new light either way.
- S04, judgment call: the threshold guards against the rounding of a still frame's pixel place at sizes such as 72 or 1280 pixels (other tap weights near 1e-5); the tests' still frames give those taps a weight of exactly 0.
- S05, equivalent: a tap of weight 0 adds 0 to the mean and to the weight sum and never passes the footprint threshold; the check only keeps the read off the texel of a failed tap, which can lie outside the image.
- S06, equivalent: a tap of weight above 0 passed the surface test, so it shows a surface, whose record holds a light of 0 or more.

Coverage: the change is GLSL only, which has no coverage tool; the mutant table above stands for it. No C# of Core, Simulation or Physics changed, so no Stryker run.

Review (reviewer agent): no correctness defect. Fixed: the reprojection wording (age, pending draws and range from the nearest tap, last light from the taps' mean), the footprint's definition (nearest tap plus taps of weight 1/64 or more), the renames LeastFootprintWeight and weightSum, the walk test's premise (58,385 pixel moves above the share) and the helper's name AwayFromARowDepthEdge, and a test of a real change under motion (B1). Not done: one tap set for both the mean and the band (Q3, polish; the mean stays inside the range either way), and an FSR case of the walk test (F3; the cabin measurement runs under FSR).
