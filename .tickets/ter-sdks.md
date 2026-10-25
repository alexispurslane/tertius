---
id: ter-sdks
status: open
deps: [ter-b3h5]
links: []
created: 2026-10-25T18:34:36Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering]
---
# Crease shadowing: XeGTAO's tuned set and a dramatic default radius

The user chose XeGTAO's tuned set over the physical constants on 2026-10-25 (open fork 1 of docs/plans/ambient-occlusion.md, option B), after the captures of ter-b3h5. Build option B as the one set of constants, by the doc's section "XeGTAO's heuristics": the radius times 1.457, the sample weight falling over the outer 61.5 % of the radius, steps bunched by a power of 2, the result raised to 2.2, the small-radius term, the slope fudge and the floor of 0.03. The C# copy follows one for one, and the wall test pins the tuned value instead of one half. Then the orchestrator picks the default radius from captures of the tuned set at several radii. The user's words: choose whatever feels most dramatic, but not overly so.

## Acceptance Criteria

The shader and the C# copy use XeGTAO's tuned constants, the tests pin the tuned values, the default radius is the orchestrator's pick from captures, and ARCHITECTURE.md and the design doc match the code.

