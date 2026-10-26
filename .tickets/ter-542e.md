---
id: ter-542e
status: closed
deps: [ter-cnam, ter-iz58]
links: []
created: 2026-10-25T16:58:27Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-s9cv
tags: [rendering, lights]
---
# Lights: the shadow atlas on the GPU

Part 6 of docs/plans/local-light-shadows.md on branch ter-s9cv-light-shadows, "Order of the work". ShadowAtlas, LocalShadowPass, frame set binding 15, G-buffer set binding 8, and tileShadow.

## Acceptance Criteria

The part's tests pass in Checked and Release, with hand mutations of each new test, as the part's "Done" says.


## Notes

**2026-10-26T18:00:53Z**

2026-10-26: done on ter-s9cv-light-shadows (Part 6 c67c1215, joined and review-fixed in 37f99148). The tile pass's slope-scaled bias is 2.5, measured, settled by the orchestrator against decision 33's 1.5 (acne at 30 and 60 degrees with 1.5); a contact test bounds it from above.
