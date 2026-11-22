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

**2026-11-22T17:27:05Z**

2026-11-22: the probe's glow change is passed to the combined check's lead (commits 68239c0af and a01ec40d0 on t3/measure-regional-lag-tolerance). It builds M2 with main's version of the patch first and tells me, because I bring that file to main after its build. Then the fair cabin switch at 2/3 and the jamb frames after the switch on M2 and X2.

**2026-11-22T17:28:10Z**

Blocker, 2026-11-22: the disk has 37 GiB free, under the orchestrator's limit of 40 GiB for a new build (57 this morning, 47 at 11:08, 43 at 11:45). The combined check's lead stopped before its two builds with the new probe. Without a build it checked on a temporary index that main's version of the probe patch applies to main 284025851 as it is, and that the line's version applies to the merged head a7e373197 with the one known fit (the Count hunk reads 12 to 14). The line's own scratch data is about 7.5 GiB (3.2 in /private/tmp/t0-5tyj, 1.7 in /private/tmp/t0-gbgj, and three worktrees), so deleting it would not reach 40 and would destroy evidence. I passed the question up with two options: free the disk to 40 GiB, or allow the two builds below 40 with a measured peak and a floor. I did not lift the limit.

**2026-11-22T17:33:11Z**

2026-11-22: the combined check's lead built M2 (main 284025851) with main's version of the probe patch and its constant-scale scratch patch: 0 warnings, 0 errors, 25 s; the disk went from 37.3 GiB free to 37.1 at the lowest sample. The off draw works (log line and frames 220 and 380). Run: /private/tmp/t0-gbgj/runs/scg-M2-23. I then committed the file on main as ee9dec413. X2 builds next.

**2026-11-22T17:40:25Z**

2026-11-22: the combined check's lead takes one half of the jambs' two by two before its fair-switch takes: M2 at a constant 0.956 through the acceptance lead's replay path (the dynamic setting), frames 10 before off and 195 after off, and from its existing frames the whole frame at 0.956 beside 2/3. Reason and the other half: see t0-5tyj.

**2026-11-22T17:46:13Z**

The combined check's half of the jambs' two by two, 2026-11-22: M2 (main 284025851 with the layer) at a constant 0.956 through the acceptance lead's replay path (build m2r: main, main's glow probe patch and scratch-dynamic-scale.patch from a01ec40d0; 0 warnings, 0 errors; disk 33.9 GiB free before, 33.7 lowest). Jamb crop's display-linear mean, fixed 0.956 / replay 0.956 / fixed 2/3: 10 before off 0.0446 / 0.0446 / 0.0435; 20 after off 0.0131 / 0.0131 / 0.0100; 60 after off 0.0065 / 0.0065 / 0.0038; 195 after off 0.0066 / 0.0066 / 0.0032; 20 after on 0.0283 / 0.0283 / 0.0275; 95 after on 0.0470 / 0.0470 / 0.0430. Replay against fixed: 876 to 2,886 pixels differ per frame by at most 4 of 255 steps, and the back wall's corners lie 451.12 px apart on both. So with the layer the jambs are grey whatever path the scale takes; the path does not make the bright jambs, and the layer is the likely cure. What stays at 0.956 with the layer on M2 and X2 and is gone at 2/3: single bright specks on the ceiling (about 5 at frame 150, about 10 at 355), a bright thin vertical line along the inner edge of the near openings, a row of bright dots along the lower sill, faint grain on the floor's left side at 355. Pictures: pics/jamb-m2-fixed-0956-vs-23.png, jamb-m2-replay-0956-vs-fixed.png, jamb-sheet-replay-jamb.png. The fair switch on M2 and X2 at 2/3, draws to settle off / on: whole image M2 122 / 53 and X2 66 / 30 (old switch 77 / 54 and 42 / 30); slowest region M2 142 / 92 and X2 77 / 50 under both switches. The jamb and floor crops are bit-identical between glow off and glow stays in all 24 frames, so the glow does not cause the jamb faults. Answers that I gave myself: the lead files a bug under ter-8hyd for the faults that stay at a scale near 1 with the layer; the stills sample at a constant 0.956 (rect-lamps, exit-cabin, deck-cuts, bay-sunbeam) moves to this lead and runs on M2 and X2, because those sides are the code that would merge.

**2026-11-22T17:46:49Z**

Per-region table of the fair cabin switch at 2/3 from the combined check's lead, 2026-11-22 (draws to settle off / on, M2 then X2): whole 122 / 53 and 66 / 30; r00 141 / 92 and 77 / 50; r01 96 / 5 and 51 / 4 (old switch 14 / 12 and 8 / 7); r02 142 / 92 and 74 / 50; r10 131 / 87 and 72 / 47; r11 116 / 83 and 63 / 44; r12 131 / 87 and 72 / 47; r20 129 / 50 and 76 / 31; r21 115 / 41 and 66 / 25; r22 129 / 51 and 75 / 31. Only the whole image and r01, the region that holds the switched panel, differ from the old switch. X2 settles about twice as fast as M2 in every region. Chart pics/switch-r11-glow-off.png. No report went up for this table, because its headline numbers were already reported.

**2026-11-22T18:07:58Z**

2026-11-22: added take for the combined check's lead: the cabin's settled lamps-on frame on M2 at fixed scales from 0.50 to 1.0 in steps of 0.05, plus 0.70 and 0.956, and on X2 at 0.70 and 0.956, with the jamb crop's mean per size. Reason: on the code without the layer the sun leaks onto the jambs at 0.70 and 0.956 only (see t0-5tyj), and the layer's cure is measured at 0.956 alone.

**2026-11-22T18:08:38Z**

2026-11-22: the sweep in steps of 0.05 is replaced by the acceptance lead's size ramp over every render size (see t0-5tyj). The combined check's lead runs it on M2 with its m2r build and on X2 with one more small build with the replay patch.

**2026-11-22T18:09:45Z**

Interim from the combined check's lead, 2026-11-22: the bug ticket for the faults that stay at a render scale near 1 with the layer is t58-i3sn (under ter-8hyd, in the lead's worktree). X2's replay build x2r is ready (0 warnings, 0 errors; disk 34.9 GiB free before, 34.8 lowest); with m2r both wait for the acceptance lead's ramp tool. Binaries: /private/tmp/t0-gbgj/bins/m2r/probe-bin and /private/tmp/t0-gbgj/bins/x2r/probe-bin. The deck's timing so far: round 1 X2 minus M2 +0.138 ms; round 2 M2 19.617 ms and X2 19.495 ms, both started under a load of 8; mean difference over two rounds +0.008 ms against the allowance of +0.30 ms. Round 3 waits for the lock and a load under 8. The stills sample at 0.956 runs.

**2026-11-22T18:17:22Z**

Order after main's walk fix, 2026-11-22. The orchestrator: main and origin are at a819b10b2; the engine change ends at 8c52592de (Shaders/screen_ray.glsl, Shaders/screen_walk.glsl, Shading/ScreenRay.cs; 37 lines added, 20 removed); the screen walk keeps the ray's inverse depth that a descent solved, so rays no longer miss a sheet that faces the eye or a far slab. The side "main with the layer" of every take that is not yet running becomes main a819b10b2, and the merged head gets main merged in again. Takes that already ran stay, marked with their commit. If a fault at a scale near 1 is gone on the new main, t58-i3sn is the back-face line's closed work.

Sent to the combined check's lead: M3 = main a819b10b2, X3 = its merged head with a819b10b2 merged in again; replay builds of both with the fair-switch probe; the size ramp runs on M3 and X3; the stills behind the crops of t58-i3sn are taken again at 0.956 on M3 and X3, each fault reported as gone, weaker, the same or stronger.

My answers (35 to 37): timing round 3 stays on the pair M2 and X2, so that it averages with rounds 1 and 2. No new run of the Checked suite for X3 unless the merge conflicts in code, because the line's merge of main gets the full suites when the wobble's fix is on main too. The build outputs m2r and x2r may be deleted if the disk is short, because the commit and the record's patch give them again.

**2026-11-22T18:38:33Z**

The size ramp tool, 2026-11-22. The acceptance lead committed it as b53c037f1 on t3/measure-regional-lag-tolerance: docs/research/design-x-acceptance-2026-11-22/tools/sizeramp.sh (the run) and sizeramp.py (the sizes, the script, the replay, the measure). Command: bash <acceptance worktree>/docs/research/design-x-acceptance-2026-11-22/tools/sizeramp.sh <probe build folder> <out folder> 40. I read both files. render_sizes() repeats UpscalingSetting.RenderExtent for 1920 x 1080 (each axis floor(pixels * scale) in single precision, down to a multiple of 8) and gives 181 sizes at the scales 0.50 to 1.0; 40 draws per size, 7,273 draws, 184 kept frames; scene orbit, camera person, lamps on, exposure 4.08 stops, shared GPU lock. A size leaks when the display-linear mean of the right jamb crop (x 1770 to 1807, y 495 to 584) or of the left jamb crop (x 113 to 145, y 500 to 579) is above 0.08. It writes ramp.txt and ramp.png and keeps only the frames of leaking sizes. After the wobble's second step on main, render_sizes() is the one function to replace.

Passed to the combined check's lead for M3 (main a819b10b2) and X3 (the merged head with it): it runs the script by its path from the acceptance lead's worktree, checks the two files against b53c037f1 first, and writes the tool's commit beside its result. My answer (38): one copy of the tool, in the acceptance record; a copy in the combined check's record only if the harness refuses a script outside the lead's worktree. My answer (39): the combined check starts with 40 draws per size and does not wait for the tool's check on main without the layer; if that check asks for more draws, I send the number.

The tool's own check on main c4c1f1f2f (does it find 1344 x 752 and 1832 x 1032, do 40 draws settle the jamb) waits for the GPU lock.

**2026-11-22T18:52:59Z**

The size ramp with the back-face layer, from the combined check's lead, 2026-11-22. Side M3 = main a819b10b2 (with the layer and the walk's stored inverse depth), build m3r with its tree's probe patch and scratch-dynamic-scale.patch, tool sizeramp.sh of b53c037f1 run by its path, 40 draws per size, played-back sizes equal the replay. Pictures in the lead's worktree t3-52bfcd29-81249085ce80dd9f under docs/research/t0-gbgj-combined-check/pics: ramp-M3.png (the chart), ramp-M3-openings.png (the near right opening at 1800 x 1008, 1872 x 1048, 1600 x 896, 1344 x 752 and 1920 x 1080), opening-0956-m2-vs-m3.png. Table: docs/research/t0-gbgj-combined-check/ramp-M3.txt. I looked at the three pictures and read the table.

Measured: 32 of the 181 render sizes leak with the layer (a jamb crop mean above 0.08): 1000x560, 1008x560, 1072x600, 1080x600, 1200x672, 1272x712, 1344x752, 1376x776, 1400x784, 1416x792, 1432x808, 1472x824, 1488x832, 1544x864, 1560x872, 1600x896, 1616x904, 1616x912, 1632x912, 1672x936, 1688x944, 1744x976, 1744x984, 1760x984, 1800x1008, 1800x1016, 1816x1016, 1816x1024, 1832x1024, 1872x1048, 1872x1056, 1888x1056. The brightest, right / left: 1800x1008 0.2298 / 0.2420; 1872x1048 0.2073 / 0.2390; 1600x896 0.1789 / 0.2099. Without the layer the bright jambs measured up to 0.77. 1832x1032 (the scale 0.956 of the earlier takes) gives 0.0491, so those takes saw grey jambs; 1832x1024 gives 0.0805 / 0.0888; 1344x752 gives 0.0876 / 0.0890. Settle check: 1344x752 right 0.0836 then 0.0876; 1832x1032 0.0482 then 0.0491; 1920x1080 0.0449 then 0.0444.

My own reading of the table: all 32 leaking sizes are among the 74 that leak without the layer. All 8 sizes of the exact 16:9 aspect sit on the floor (0.043 to 0.047). 17 more sizes measure 0.06 to 0.08, so 49 of 181 are above 0.06. In the crops a leaking size shows a broad bright line at the opening's inner edge with a glow onto the jamb face and into the dark opening, and dots along the sill; 1920x1080 shows a thin line and a clean face.

Result: the layer weakens the jamb leak and does not cure it. The jambs are not closed. It goes into t58-i3sn and up to the orchestrator for the back-face line.

The crops of t58-i3sn at 0.956 on the new main: every fault is the same on M3 as on M2 (specks per box ceiling / opening / floor's sides at film frames 150, 220, 355: M2 5/39/0, 10/36/41, 10/37/30; M3 5/39/0, 10/36/42, 10/37/31). So the walk fix did not cure them, and t58-i3sn stays an open bug.

X3 = the lead's merge bcca31e95 (main a819b10b2 merged into the merged head; only the probe patch conflicted; resolved with the line's fair-switch probe, Count 12 to 14). The hook's Rendering tests passed: 3,411 total, 0 failed, 10 skipped. X3's ramp runs.

Deck timing, four valid rounds under a load of 8: X2 minus M2 = +0.138, -0.122, +0.220, +0.267 ms; mean +0.126 ms, median +0.179 ms; every round under the allowance of +0.30 ms.

The stills sample at 0.956 (M2 and X2 alike): rect-lamps specks rise from 0.005 to 0.024, all at the openings; deck-cuts specks rise from 0.005 to 0.025 around the glass hall's frames, and a bright streak on a pillar's lower edge is a little longer than at 2/3; exit-cabin no change; bay-sunbeam better, specks fall from 0.14 to 0.01 because the slit's teeth of 2/3 are gone at 0.956.

Found: the lead's record folder and t58-i3sn.md were untracked in its worktree. I told the lead to commit them now and after each result.

**2026-11-22T18:54:23Z**

Order from the orchestrator, 2026-11-22: do not wait for the merge; the combined check's lead runs the size ramp now on the wobble fix's branch t3/fix-projection-aspect-wobble (step 1 of ter-qt72: the projection's and FSR's aspect from the output extent), in a build of that branch alone, and also takes the lamp switch under the card's played-back sizes with the two tracked edges as the wobble's before and after on the Mac. The orchestrator passes the 32 sizes and t58-i3sn to the back-face line.

Sent to the combined check's lead: side W = the branch's head (839bce1b5 at 13:54: 0495319e3 plus main ee9dec413; it holds the layer and the fair-switch probe, not the walk fix; it changes SceneBatcher.cs, Fsr3Constants.cs, FrameHistory.cs, UpscalePass.cs and VulkanRenderer.cs). Take 1: the ramp on W, 181 sizes, 40 draws each; the count of leaking sizes beside 74 and 32. Take 2: the fair cabin switch with one played-back size log on m2r (before) and on W (after), measured with wobble.py (edge distance, midpoint, correlation with the rounded aspect), the level's rise at size-change draws, and the new boil measure; one side-by-side video.

My answers (40 to 42): the before build is m2r (main 284025851), because the fix branch does not hold the walk fix. The played-back log is the card's log of the Design X side at the 10 ms target (switch-x-10, scales 0.643 to 0.756, 84 changes), on both builds, because it has the most size changes and one log on both builds makes a true before and after. The take gets a side-by-side video besides the numbers, because the wobble is a fault over time.

**2026-11-22T18:58:17Z**

A new reading of the jamb leak from the back-face line, passed by the orchestrator on 2026-11-22, with one short test. Chart: /Users/alexispurslane/.t3/worktrees/tertius/t3-1b834d57-evidence/jamb/jamb-light-against-aspect-error.png (I looked at it). All 181 sizes of ramp-M3.txt lie on one curve of the aspect error, (render width / render height) / (16/9) - 1, whatever their width. The curve is a sawtooth with jumps at about -0.34 %, +0.45 % and +1.25 % that fall back to the floor within about 0.3 %. Candidate cause, read from the code and not measured: the texel grid of the sun's shadow cascades, whose texel size depends on the field of view and the aspect (ShadowCascades.cs) and whose center snaps to a grid fixed in the world. Consequence: the wobble's fix pins the aspect at the window's, so the ramp on its branch can count zero at 16:9 without a cure. The period of 72 px that I saw in the widths is this curve seen along the width.

Sent to the combined check's lead, after the running ramp and before the wobble's before and after: at a render scale of 1, windows of 1800 x 1008 (aspect error +0.45 % by the window's shape) and 1920 x 1080 on main a819b10b2 and on the fix's branch; the reading predicts a jamb light near 0.24 at 1800 x 1008 on both, and a floor value near 0.045 refutes it. Then, if cheap, the field of view in small steps on main.

My answer (43): if the field of view has no switch in the script or the probe, the lead writes no engine code for it and takes windows of the height 1008 and the widths 1784, 1792, 1808 and 1816 on main in its place (aspect errors -0.45 %, 0, +0.89 %, +1.34 %).

**2026-11-22T19:05:57Z**

The size ramp on the merged head, from the combined check's lead, 2026-11-22. Side X3 = the lead's merge bcca31e95 (Design X with main a819b10b2), build x3r, tool sizeramp.sh of b53c037f1 by its path, 40 draws per size, played-back sizes equal the replay. Chart: docs/research/t0-gbgj-combined-check/pics/ramp-X3.png, table ramp-X3.txt, in the lead's worktree t3-52bfcd29-81249085ce80dd9f. I looked at the chart.

Measured, M3 (main a819b10b2) / X3: sizes above 0.08: 32 / 35. Sizes above 0.06: 49 / 54. Largest jamb mean, both at 1800 x 1008, right / left: 0.2298 / 0.2420 against 0.2339 / 0.2996. Settle check 1344 x 752 right, 10 draws before the last frame / at it: 0.0836 / 0.0876 against 0.0831 / 0.0861; 1920 x 1080 right: 0.0449 / 0.0444 against 0.0458 / 0.0455. The lists differ at five sizes, each within 0.012 of the threshold: only M3 leaks at 1376x776; only X3 leaks at 1120x632, 1144x640, 1216x680 and 1904x1064. Per size, X3's larger jamb mean minus M3's has a median of +0.0014 and ranges from -0.0134 to +0.0576 (the left jamb at 1800 x 1008).

Result: the teeth are the same on both codes. Design X adds no leaking size of its own; it sits a little higher on a few teeth. My reading: the leaked light is the same fault of main on both sides, and Design X's wider clamp keeps a little more of its bright samples at a held size.

The fix branch's build W from 839bce1b5: its probe patch and scratch-dynamic-scale.patch apply unchanged; 0 warnings, 0 errors; disk 34.0 GiB free. W's ramp runs next, then the window test (no field-of-view switch exists, so the cheap form with the widths 1784, 1792, 1808, 1816 at the height 1008), then the wobble's before and after.

**2026-11-22T19:20:51Z**

The size ramp on the wobble fix's branch and the window test, from the combined check's lead, 2026-11-22. Side W = t3/fix-projection-aspect-wobble at 839bce1b5 (step 1 of ter-qt72), build wr with its probe patch and scratch-dynamic-scale.patch, both unchanged. Pictures in the lead's worktree t3-52bfcd29-81249085ce80dd9f under docs/research/t0-gbgj-combined-check/pics: ramp-W.png and window-1800x1008-M3-W.png. I looked at both.

Measured, the ramp on W (181 sizes, 40 draws each, played-back sizes equal the replay): 0 sizes above 0.08 and 0 above 0.06, against 74 without the layer, 32 on M3 (main a819b10b2) and 35 on X3. Largest jamb mean 0.0468 (right, 1088 x 616); median of the larger jamb per size 0.0441. At 1800 x 1008 W gives 0.0441 / 0.0439 where M3 gives 0.2298 / 0.2420. Settle check: 1344 x 752 right 0.0446 then 0.0443; 1920 x 1080 right 0.0447 then 0.0443.

Measured, the window test (the ramp's view, the render scale held at 1 through the replay so that the render size equals the window, 40 draws; crop boxes scaled by x' = W/2 + (x - 960) * H/1080, y' = y * H/1080, checked on the frame), right / left jamb mean:
- M3 1800 x 1008 (aspect error +0.45 %): 0.0977 / 0.0582
- M3 1920 x 1080 (0): 0.0436 / 0.0435
- M3 1784 x 1008 (-0.45 %): 0.0437 / 0.0437
- M3 1792 x 1008 (0): 0.0438 / 0.0433
- M3 1808 x 1008 (+0.89 %): 0.0435 / 0.0431
- M3 1816 x 1008 (+1.34 %): 0.0510 / 0.0591
- W 1800 x 1008: 0.0977 / 0.0582
- W 1920 x 1080: 0.0436 / 0.0435
No field-of-view switch exists, so the cheap form with window widths ran.

Results: the back-face line's reading holds in its main point. A window whose own shape is off 16:9 by +0.45 % leaks with no dynamic scale, and it leaks the same on the fix's branch (equal to four decimals). So the ramp's zero on W means "hidden at 16:9", not "cured". The fix pins the aspect at the window's, and a window off 16:9 can sit on a tooth all the time. Against the predictions: 1792 and 1808 on the floor as predicted; 1816 above the floor and under the threshold, near a tooth as predicted; 1784 on the floor against the prediction of a tooth near -0.34 % (sizes between were not tested). The leak in the window (0.098 right) is weaker than at the same render size on the ramp (0.230), where FSR upscales 1800 x 1008 to 1920 x 1080.

Tool defect, small: ramp.png scales its axis to the largest mean, so a ramp without a leak draws no threshold line (ramp-W.png ends near 0.05).
