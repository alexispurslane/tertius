---
id: ter-xgra
status: in_progress
deps: []
links: []
created: 2026-11-01T16:01:19Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, screen-space-light]
---
# Screen-space light: find and attribute every artifact on demo-exit and the review scenes

Diagnosis ticket of docs/plans/screen-space-artifacts.md (section 'What a diagnosis delivers'). Known artifact: the band of dark brown specks along the floor's creases on the deck (ter-k96l, captures/artifacts-2026-11-01/deck-crease-specks.png and captures/deck-fringe-2026-10-31/). Build a gallery of captures at 1920 x 1080 (frames of each of demo-exit's nine films, and the review scenes of docs/plans/screen-space-illumination.md: cabin-light, bounce-raised-box, bounce-red-wall, demo-surface, mirror-balls), find every artifact of the screen-space light, and attribute each to its stage (depth pass, sectors, denoise, upsample, ambient pass, bounce) with probes and the A/B switches. No fix goes into the code.

## Acceptance Criteria

A ticket note lists every artifact found, with its crop, its stage, the probe evidence, whether it is a fault or a limit of the method, and the candidate fixes with their cost. The probes are not in the commits.


## Notes

**2026-11-01T16:17:34Z**

2026-11-01: workflow run f7a51999-484a-4aa5-af75-d042ed9f5df9 (version 869b0b07), branch ter-xgra-screen-space-light-diagnosis from 79d27a68 (main after the ter-pjza merge). Brief: /tmp/briefs/xgra.txt. Build = the diagnosis, review = an adversarial check of each attribution against its probe evidence, fix = the review's findings.
