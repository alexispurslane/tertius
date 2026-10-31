---
id: ter-r5hb
status: open
deps: []
links: []
created: 2026-10-31T23:33:23Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, reflections, hair]
---
# Hair reflections: measure MaxSteps 256 on the review scenes, then remove the hair cut of decision 35 if the cost is small

ter-a9hh (2026-10-31) found that grazing hair rays inside the surface fade's range do not hit their own card: they run out of MaxSteps (128). Hair rays there need 65 to 256 steps, because their images are 200 to 400 px long. With the cut of decision 35 removed and MaxSteps 256, hair-rugs' tilted rug fades smoothly to a cosine of 0 (band.py: rows 413 to 419, 0 steps, 0 isolated pixels). Probes, diffs and captures: ter-a9hh's notes and /tmp/ter-a9hh. The user left the choice to the orchestrator, who chose to measure first and then remove the cut (2026-10-31).

## Design

1. Measure the GPU cost of MaxSteps 256 against 128 on the review scenes of the reflections (docs/plans/screen-space-reflections.md, Part 6) and on demo-exit's deck window, with a drift-cancelling method: paired traces or interleaved builds with a control, under the GPU lock. Report the reflection pass and the frame. 2. Recheck decision 31 (128 as the fewest steps that look clean) with the captures of those scenes at 256. 3. If the frame cost stays within the measurement's noise, or under a share that the ticket note justifies against ter-pq5y's 12 ms goal, remove the hair cut from reflectingPixel, let hair keep the surface fade like every other model, and raise MaxSteps to 256. Update decision 35 and decisions 31, the reflections plan and ARCHITECTURE.md. 4. If the cost is not small, keep the cut, record the numbers, and stop: that is a fork for the user.

## Acceptance Criteria

A ticket note gives the paired cost numbers and the method. Either the cut is gone with MaxSteps 256, tests updated red first (the hair cut's tests now pin the fade), snapshots reviewed with the reference, actual and FLIP images, and screenshots of hair-rugs; or the numbers show why the cut stays.

