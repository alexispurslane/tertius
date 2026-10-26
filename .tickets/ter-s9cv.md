---
id: ter-s9cv
status: closed
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

**2026-10-26T20:35:11Z**

2026-10-26: merged into main in ef755699, after the merge of main into the branch (14aff809) and its review fixes (205494f0). Coverage of the epic's changed lines, Debug and Release: 1851/1878 lines (98.6%), 505/530 branches (95.3%). The uncovered lines are demo scenes that no test runs, a catch that disposes ShadowAtlas when a layer view fails, and single guards. The coverage run's only failures were three Physics allocation tests, which the coverage instrumentation's own allocations break; the plain Checked and Release runs pass. Demo-exit's cost after the epic: lighting 7.3 ms against 2.0 ms and frame span about 17.4 ms against 11.7 ms at 1080p; ter-p1u7 is the epic's profile (decisions 3 and 5 of rendering-performance.md). Follow-ups: ter-p1u7, ter-gf0t, ter-mgap, ter-6pzm, ter-74nr.
