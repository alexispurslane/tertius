---
id: ter-u102
status: closed
deps: [ter-b2da]
links: []
created: 2026-11-01T03:01:55Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# gpu-trace: does the environment faces' cycle give the frame span's odd-even offset of ter-r5hb's deck controls

Found by ter-svcs, 2026-10-31. ter-r5hb's three controls of demo-exit's deck (deck-control, deck-plain1, deck-plain2 in /tmp/ter-r5hb/traces) show a paired frame span offset of about -0.11 ms between the odd and the even frames, cause not identified. The deck serves two maps, so the environment faces follow a cycle of 12 frames (ter-svcs). A scratch check (/tmp/ter-svcs/scratch/decycle.py, which subtracts the median of each phase of the cycle before it pairs the frames) moved the frame span's paired median of the odd frames from +0.141 (0.041) to +0.039 (0.048) ms in deck-plain1, from +0.088 (0.051) to -0.012 (0.040) ms in deck-control, and from +0.031 (0.065) to +0.027 (0.058) ms in deck-plain2. That fits the cycle but does not establish it: the subtraction adds noise, and 10 frames per phase are few. Test it with a deck control whose face schedule a temporary probe holds fixed, as ter-svcs did for mars-air.

## Acceptance Criteria

The frame span offset of the deck controls is explained with evidence from a trace, or shown not to come from the face schedule.

## Notes

**2026-11-03T12:35:13Z**

Closed on 2026-11-03 at the user's request, without its work merged: it belongs to the pre-temporal renderer (spatial reuse, the 12 ms performance push, or the old reflection and bounce pipeline), and epic ter-89xq, the temporal renderer, supersedes it. Its notes and any branch or worktree stay for reference.
