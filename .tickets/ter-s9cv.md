---
id: ter-s9cv
status: in_progress
deps: [ter-zzyr, ter-2t5o, ter-63wr, ter-ptft]
links: []
created: 2026-10-11T13:52:12Z
type: epic
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, long-term]
---
# Point and spot light shadows, and lights instead of one sun

Design: docs/plans/local-light-shadows.md.

On 2026-10-11 the user folded a second goal into this epic: the sun stops being a special case. Every far light, such as a second star, gets what the sun gets now: far shadows against its own disc, cascaded shadow maps within a budget, and baking into impostor captures. Its brightness comes from its power over its distance squared. The design doc's section "Lights instead of one sun" lists the points that still need two designs.

On performance, the doc's plan holds for local lights: one shared atlas, a tile budget ranked by importance, cached tiles for lights and casters that do not move, culling by range and cone, and GPU timings for 4, 16 and 64 shadowed lights. Far lights add four cascades each, so only the brightest one or two get them.


## Notes

**2026-10-22T00:09:45Z**

2026-10-21: the user chose the design of the part "Lights instead of one sun" from two designs: SI units, one light list for the renderer, a reach derived from the power. It is now ticket ter-bpvl, which waits for automatic exposure (ter-zzyr). The design doc holds the full design, with every reader and writer.

**2026-10-25T16:59:42Z**

2026-10-25: the design doc is refreshed against main at 5479c5f1 (branch ter-s9cv-light-shadows, 60b86c6c and 62b0d35b) and split into seven part tickets: ter-3att, ter-cl86, ter-cnam, ter-iz58, ter-wjib, ter-542e, ter-t1cp. Lights instead of one sun comes first. The user chose physical stars in SI units (fork 1, A). The orchestrator kept pure ranking at the tile budget's edge by the user's rule 'pure model first' (fork 2, A).

**2026-10-26T18:15:01Z**

2026-10-26: coverage of the changed lines at 37f99148 (Parts 1 to 6), Debug and Release: lines 1528/1564 (97.7%), branches 473/498. Uncovered: demo scene numbers that only headless runs read (DemoScenes.PbrSpheres, Puffs, Reflections); UnreachableException arms in SceneBatcher and RenderSnapshotBuilder; ShadowAtlas's dispose-and-rethrow when a layer view fails; DescriptorLayouts' throw for a device with too few storage buffers (StorageBufferProblem itself is tested); FrameResources' rewrite of the tile buffer after it grows; ImpostorManager's arm for a light with atlas tiles (an impostor lit by a shadowed lamp).
