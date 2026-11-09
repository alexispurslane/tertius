---
id: tb-8zew
status: in_progress
deps: []
links: []
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

