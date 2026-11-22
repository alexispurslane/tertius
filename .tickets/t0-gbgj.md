---
id: t0-gbgj
status: in_progress
deps: []
links: []
created: 2026-11-22T11:15:34Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# The combined check of Design X with the back-face layer: the line's head merged with the new main, in the cabin, rect-lamps, the bay film and the deck's frame time

From the parent orchestrator, 2026-11-22. The back-face line (branch ter-kh4g-back-face-depth-layer, a8e34541a) is finished, and its merge into main waits for the user's decision. It changes the hit decision of the screen walk, not the accumulation. Its own takes show rect-lamps the same within 1 %, the deck at the noise floor, and the cabin's window frames brighter by 20 of 255 levels. Its cost is +0.18 to +0.27 ms in rooms on the M1 Max and +0.75 ms in the crowd.

The acceptance record of Design X (t0-5tyj) compares the line's head with main c4c1f1f2f, and it stays valid as the record of Design X against its base. This ticket is one short check for the time after both merges are decided, not a second full record.

Start it only when the parent orchestrator says that main moved.

The check: merge the new main into the line's branch. On that merged head, against the new main:
- The still rect-lamps and the switch cabin (the room of scene orbit with the camera person), with the still's specks, blotch and daub and the switch's draws to settle.
- The film rect-bay: level, last-frame specks, flicker and lag error.
- The deck's frame span, three rounds in turn at a low load. The two allowances add: Design X's 0.30 ms and the layer's own cost.
- The Checked suite and then the Release suite.
Pictures first, each beside the new main. A take where the merged head is worse than the record of t0-5tyj says so plainly.

Acceptance: a short record in docs/research with the four takes and both suites, and no new fault by the user's bar against the new main.


## Notes

**2026-11-22T13:58:30Z**

Start and design (line orchestrator, 2026-11-22). The parent says that main moved: the back-face depth layer is on main at 284025851 (the last commit that changes code is f7d606fec). The frame's order is now G-buffer, back faces, depth pyramid, tracer. The depth pyramid's build turns the back share into each pixel's slab (slabBack in depth_pyramid.glsl), and the walk's 10 % thickness rule is gone (thickness.glsl is deleted). One more fix of the walk from the back-face line is coming. This ticket does not wait for it.

The merge. A trial merge of 284025851 into the line's head 880d0a335 gave one conflict, in ARCHITECTURE.md, at three places. Everything under src merged without a conflict, but nobody built it. The lead makes the merge on its branch, and I merge that branch into the line after the check. My resolution of the three places is the file /private/tmp/t0-gbgj/ARCHITECTURE.resolved.md:
- The list of known limits: keep main's two new items (the slab of no depth, the thin closed plate). Leave out the two items that the line removed, because Design X ends them (ter-18hc and td-hjn6).
- The row of shaderStorageImageExtendedFormats and the row of the storage image formats: keep the line's R16_UINT facts (the surface test image and the sample ages, the 16-bit table, the SurfaceHistory sentence) and main's facts of the depth pyramid (its levels in RG32F, the finest level among them, and R32F for the light pyramid's levels and the device depth).

The sides:
- M2 = main at 284025851, with main's survey probe patch.
- X2 = the merged head, with the survey's probe patch fitted to it. The fix of the probe patch for the remedy is commit 0e441286d on the acceptance lead's branch t3/measure-regional-lag-tolerance (bug tc-w3c0). Bring it with a cherry-pick, then fit the patch to the layer where it does not apply.
- The record of Design X alone (t0-5tyj, against main c4c1f1f2f) is the comparison for "did the layer change what Design X gives".

The takes, each on M2 and X2 through the survey:
1. Stills: rect-lamps at the automatic exposure and at 4.08 stops, exit-cabin, docking-cabin, cabin-light-cabin, deck-cuts and bay-sunbeam. Specks, blotch, level, and the daub for rect-lamps. The parent says that the cabin's jambs between the pillars turn from black to lit under the layer, and that the lamp hall and the deck look alike.
2. The switch cabin: the draws to settle per region, the levels 60 draws after each switch, and r11 before the switch.
3. The film rect-bay: level, last-frame specks, flicker, and the lag film bay-sun's lag error.
4. The deck's frame span: three rounds in turn of M2 and X2 under the exclusive lock at a low load. Design X's allowance is +0.30 ms above main, and both sides now hold the layer.
5. The Checked suite and then the Release suite on the merged head.

Acceptance: the record docs/research/t0-gbgj-combined-check/README.md with the pictures first, each beside M2, and one table "X2 against M2, and against the record of Design X alone". No new fault by the user's bar against M2. Both suites pass. A take where the merged head is worse than the record of t0-5tyj says so plainly.
