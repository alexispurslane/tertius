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

**2026-11-22T14:01:31Z**

Launch of the combined check lead (2026-11-22): clientRequestId design-x-line-t0-gbgj-lead-1, task node:delegated-task:command%3Amcp%3Aa061e2e4-17a4-45d5-ac91-153bf5752d2c%3Adelegate-task%3Adesign-x-line-t0-gbgj-lead-1, child thread thread:delegated-task:command%3Amcp%3Aa061e2e4-17a4-45d5-ac91-153bf5752d2c%3Adelegate-task%3Adesign-x-line-t0-gbgj-lead-1. Target claudeAgent, claude-opus-5-5, effort high, own worktree. The lead makes the merge of the line head 795e1661f with main 284025851 on its own branch, with the resolved ARCHITECTURE.md from /private/tmp/t0-gbgj/ARCHITECTURE.resolved.md, then runs the takes of the design note. Cloud budget 0.50 dollars. Stage chain: merge and build, Checked suite, commit, probe patch, takes, Release suite, record, review, fix.

**2026-11-22T14:23:49Z**

The user on 2026-11-22, on the walk chart: "I would rather a video of both, that is easier to judge". The orchestrator set the rule: a fault that shows over time (a trail, a fade, a flicker, a crawl) goes to the user as a side-by-side video, and a sheet of frames is for a fault that stands still. Form: both sides in one frame with labels, real frames at 60 frames a second with one draw per frame, one fixed exposure, 1920 x 1080 per side if the file allows it, H.264 mp4, yuv420p, CRF 12 or lower, no denoise filter. No new take only for a video, except for the walk. Answer that I gave myself: a video of at most 20 MB is committed, and a larger one stays untracked in the record folder with its path and size in the README. Sent to the combined check lead as a steer (design-x-line-t0-gbgj-video-preference): a video for the film rect-bay and for any other take of it where a difference shows over time.

**2026-11-22T14:26:41Z**

Early result (a) of the combined check lead, 2026-11-22: worktree /Users/alexispurslane/.t3/worktrees/tertius/t3-52bfcd29-81249085ce80dd9f, branch t3/combined-renderer-check, started at main 284025851. The merge of 795e1661f is b1a433aae; only ARCHITECTURE.md conflicted and my resolved file went in unchanged. The merged head builds with 0 warnings and 0 errors, and the Checked suite passed on the staged merge: 11,141 tests, 0 failed, 28 skipped. No change under src or tests was needed. The probe patch: cherry-pick e867afcc1, and the fit a7e373197 changes one hunk, because the layer adds the specialization constant BothFaces and SpecializationStorage.Count is 12. Bug t58-k95j: main's own probe.patch has the same stale hunk, so the survey's probe build fails on main 284025851. Only the film rect-bay gets a video, because the switch and the lag film keep region series and no frames.

**2026-11-22T14:31:46Z**

From the orchestrator, 2026-11-22: main moved to 2ffd7e66f by a tool fix only. The back-face line fixed main's probe.patch (Count 12 to 13) and put t58-k95j on main, closed. The combined check's sides stay main 284025851 and the merged head. For my later merge of main: the patch conflicts on one added line and the line's 14 is right (two probe rows), and main's copy of t58-k95j is the ticket. I told the lead to delete its uncommitted copy and not to merge 2ffd7e66f. Still coming on main: the walkScreen fix f6b55d811, which needs one more merge of main before the merge of Design X.

**2026-11-22T15:07:26Z**

Early result (b) of the combined check lead, 2026-11-22. M2 = main 284025851, X2 = the merged head a7e373197. Measured: still rect-lamps, automatic / 4.08 stops: blotch M2 0.0128 / 0.0126, X2 0.0105 / 0.0106; daub against the clamps-off reference M2 0.0119 / 0.0102, X2 0.0089 / 0.0081; specks M2 0.0048 / 0.0096, X2 0.0000 / 0.0096; light shown, ceiling / walls, M2 100.6 / 100.2 %, X2 99.5 / 99.4 % of the reference. X2 equals Design X alone (part B) within 0.0003. Switch cabin, draws to settle off / on: whole image M2 77 / 54, X2 42 / 30; r11 M2 116 / 83, X2 63 / 44; slowest region M2 142 / 92, X2 77 / 50; X2 faster in all 10 series; Design X alone was 43 / 31, 64 / 46 and 77 / 50. The other five stills: X2's blotch and daub are lower than M2's in all five; specks equal or lower except bay-sunbeam, 0.1487 against 0.1437. My reading of the pictures (rect-lamps-whole.png, switch-r11.png, layer-exit-cabin-jamb.png): rect-lamps is clean on both sides; the switch's curve of X2 returns sooner and without a wiggle; the jambs beside the cabin's window lamp are black on old main and on Design X alone, and lit on M2 and on X2. So the layer did not change what Design X gives, and Design X did not change what the layer gives.

**2026-11-22T15:15:42Z**

The combined check's video of the film rect-bay exists, 2026-11-22: docs/research/t0-gbgj-combined-check/pics/rect-bay-M2-X2.mp4 in the lead's worktree, M2 left and X2 right at the fixed exposure of -0.26 stops, 3840 x 1080, 180 frames at 60 a second, H.264 CRF 12, 11 MB. Measured by the lead: flicker M2 0.00190, X2 0.00241 (limit 0.006); grain flicker 0.00580 and 0.00619 (limit 0.05); worst sampled frame's level +1.35 and +1.01; its blotch 0.259 and 0.219. My check: ffprobe reads 180 frames, and the decoded last frame shows the bay with both labels and the counter. Passed up to the orchestrator for the user.

**2026-11-22T15:23:43Z**

The combined check's video of the film bay-sunbeam exists, 2026-11-22: docs/research/t0-gbgj-combined-check/pics/bay-sunbeam-M2-X2.mp4 in the lead's worktree, M2 left and X2 right at -4.01 stops, 3840 x 1080, 180 frames at 60 a second, H.264 CRF 12, 12 MB. Measured by the lead: flicker M2 0.00397, X2 0.00515 (limit 0.006; the acceptance record has 0.00393 and 0.00519); grain flicker 0.00951 and 0.00981; worst sampled frame's level -0.60 and -0.37, blotch 0.336 and 0.210. Lag film bay-sun: median lag M2 27.8, X2 13.6 draws; lag error 0.266 and 0.150. Film rect-bay, middle / last frame: level M2 +1.35 / +0.24, X2 +1.01 / +0.13; specks M2 0.21 / 0.97, X2 0.19 / 0.26. The middle frame's level of X2 is worse than Design X alone (+0.85), and M2 rose likewise against old main (+1.17), so the layer raises that excess on both sides. My check: ffprobe reads 180 frames, and the decoded frame 90 shows the bay with labels and counter, with the saw-toothed edge on both sides. Passed up for the user.

**2026-11-22T16:08:56Z**

Added take, 2026-11-22: the combined check's lead takes the lamp switch on M2 and X2 on the Mac at a constant render scale of 0.956 and of 2/3, with frames at six moments, for the finding of the bright window jambs of Design X in the cloud's dynamic-scale video (see t0-5tyj). The layer changes the slab of these jambs, and the merged head is what would merge. It uses the acceptance lead's scratch patch for the constant scale and its own builds.

**2026-11-22T16:23:27Z**

The user's order of 2026-11-22 changes the lamp switch of every take: a selected lamp loses its light and its glow together (the words and the orders are in t0-5tyj). The combined check's switch take of early result (b) used the old switch and is marked as the glow-stays case. Its lead takes the cabin's switch at 2/3 again with the new switch when the acceptance lead's probe hunks exist, and splits the constant-scale jamb take: the lamps-on settled frames now, the frames after the switch again later.

**2026-11-22T16:45:52Z**

Early result of the combined check lead, 2026-11-22: the jamb take on the Mac at a constant render scale, glow-stays case, M2 = main 284025851 and X2 = the merged head a7e373197. Sheets pics/jamb-sheet-glow-stays.png and jamb-sheet-glow-stays-jamb.png. Jamb crop's display-linear mean, M2 0.956 / X2 0.956 / M2 2/3 / X2 2/3: 10 before off 0.0446 / 0.0465 / 0.0435 / 0.0435; 20 after off 0.0131 / 0.0106 / 0.0100 / 0.0082; 60 after off 0.0065 / 0.0104 / 0.0038 / 0.0036; 195 after off 0.0066 / 0.0079 / 0.0032 / 0.0033; 20 after on 0.0283 / 0.0351 / 0.0275 / 0.0325; 95 after on 0.0470 / 0.0519 / 0.0430 / 0.0439. The survey's speck score is 0 in every floor crop, because its absolute floor hides dim specks. The aspect's stretch at 0.956 is +0.73 px over 451. The constant scale came from the lead's own scratch patch (TERTIUS_X_RENDER_SCALE). My reading of the sheets: with the lamps on and settled, the jambs are the same grey in all four columns, so the bright silver jambs of the cloud's videos do not appear on the Mac at a constant 0.956 with the layer, and the scale's value alone does not make them. At 0.956 both sides show soft blotches and bright texels on the jambs at 20 and 60 draws after off, X2's brighter, and at 2/3 both are dark and clean. That second fault was hidden by the takes at 2/3. This take has no side without the layer.

**2026-11-22T17:04:14Z**

Early result of the combined check lead, 2026-11-22: motion measured per frame in the kept frames of the films rect-bay and bay-sunbeam, M2 against X2 at M2's exposure (charts pics/rect-bay-motion.png and bay-sunbeam-motion.png, strips rect-bay-strip.png and bay-sunbeam-strip.png). rect-bay: the light falls smoothly in every region on both sides; specks are equal to frame 120, then M2 rises to 1.30 and X2 falls to about 0.2; grain is equal, 0.004 to 0.019; the window edge holds within 0.1 px to frame 110 and then scatters by up to 1.5 px on both sides as its contrast falls. bay-sunbeam: every region rises smoothly and X2 rises sooner; specks reach 7 in the dark first 15 frames on both, X2 clears by frame 25 and M2 by frame 45; M2 has a grain burst of 0.0525 at frames 10 to 20 against X2's 0.0362; the window edge scatters by up to 2.7 px (M2) and 2.2 px (X2) while the back wall's clouds pass it. What the lead's eye finds: fine bright grain on the left wall and the floor of rect-bay from frame 120 on both sides, M2's stronger; soft light clouds on the back wall and grain on the right wall of bay-sunbeam on both sides. By the user's rule these are bugs on both sides, and X2 lowers them without removing them. Not checked: a wobble under 0.1 px, vertical shift, dim specks under the survey's floor, color, and whether specks crawl. The deck's timing rounds 2 and 3 wait for a load under 8 (137 at the report).
