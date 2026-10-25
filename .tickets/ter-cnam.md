---
id: ter-cnam
status: in_progress
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
