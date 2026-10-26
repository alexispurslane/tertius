---
id: ter-cnam
status: closed
deps: [ter-nl5u, ter-3att]
links: []
created: 2026-10-25T16:58:27Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-s9cv
tags: [rendering, lights]
---
# Lights: the C# math of the shadow tiles

Part 3 of docs/plans/local-light-shadows.md on branch ter-s9cv-light-shadows, "Order of the work". ShadowTiles.Update, TileView, TileAim and TileDraw. It needs CubeFaces from the environment map.

## Acceptance Criteria

The part's tests pass in Checked and Release, with hand mutations of each new test, as the part's "Done" says.


## Notes

**2026-10-25T22:04:14Z**

Started on 2026-10-25 before the environment map merges into main: Part 3 needs only CubeFaces, so the branch merged the environment map's reviewed tip 8c585cf5. The three conflicts (ScriptCommand.cs, GpuTesting.cs, TestSupport.cs) keep both sides. The branch merges main again after the environment map merges.

**2026-10-25T23:11:15Z**

Done on the epic branch ter-s9cv-light-shadows on 2026-10-25: the merge of the environment map's tip (07e4466d), Part 3 (5509e0aa) and the fix of the review's eight findings (ea311271). The fix put a floor at float rounding under the cache's movement threshold, so a lamp inside a big hull keeps its cached tile, and scaled the quarter-texel thresholds by cos² of the widest angle at which a caster shows. Checked 7,937 and Release 7,925 passed, 0 failed. 57 hand mutations of ShadowTiles.cs: 56 killed, 1 equivalent removed. The epic's coverage runs after Parts 4 and 6, as the design says. The branch merges into main with the epic.
