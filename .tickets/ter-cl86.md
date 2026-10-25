---
id: ter-cl86
status: closed
deps: []
links: []
created: 2026-10-25T16:58:27Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-s9cv
tags: [rendering, lights]
---
# Lights: the settings

Part 2 of docs/plans/local-light-shadows.md on branch ter-s9cv-light-shadows, "Order of the work". LocalShadowSetting, RendererOptions.LocalShadows and CascadedLights, CheckOptions, and three script options.

## Acceptance Criteria

The part's tests pass in Checked and Release, with hand mutations of each new test, as the part's "Done" says.


## Notes

**2026-10-25T21:03:19Z**

Built, reviewed and fixed on the epic branch ter-s9cv-light-shadows on 2026-10-25: 2019869a (part 2), with the review's fixes in 434b0131 (the most cascade sets in one home, the maxImageArrayLayers refusal moved to Part 6's ShadowAtlas, the script options in ARCHITECTURE.md, one folded parser).
