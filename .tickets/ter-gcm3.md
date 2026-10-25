---
id: ter-gcm3
status: in_progress
deps: [ter-ptft]
links: []
created: 2026-10-11T13:52:12Z
type: epic
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, long-term]
---
# Anti-aliasing with SMAA 1x

Design: docs/plans/anti-aliasing.md.


## Notes

**2026-10-25T15:09:02Z**

2026-10-25, after the review: the orchestrator saw the planet's rim in station/pods-out at 12x zoom look more jagged with SMAA than without. A fixer found that the port matches the reference header: a literal CPU emulation of SMAA.hlsl in Direct3D conventions reproduces the GPU image within 0.45 levels. The rim is not a two-color edge. A 1 px limb highlight starts each run of the staircase, so SMAA sees two edges (sky to limb, limb to interior) and blends the limb as a thin feature of its own. On clean two-color edges at 16 slopes, a new per-pixel test (4e0238be) shows SMAA's error at 9 to 50 % of the error without it. Settled by the orchestrator: accept the header's behavior on the 1 px limb, because the reference is faithful and the alternatives change the planet's look or what SMAA sees; the limb's own thin band is the cause, and a later anti-aliasing of thin features (for example a temporal method) would treat it.
