---
id: t0-3vwx
status: in_progress
deps: []
links: []
created: 2026-11-22T19:45:41Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# The walk and the lamp switch under the dynamic render scale on main with the wobble's first step: videos, and whether the fix helped Design X

The user's order of 2026-11-22: "I want videos of the walk and lamp switch under normal engine conditions — that is, dynamic render scaling — because that's always on (or should be)." The user had the wobble fixed first, because it "may keep things more consistent on screen, thus helping keep longer histories". Step 1 of the wobble's fix (ter-qt72: the camera's aspect comes from the output size) is on main at ac5f23915. This ticket takes the two films again on the fixed code and measures whether the fix helped Design X under a moving render size. It is a take and a record. It changes no engine code.

## Design

Sides. M5 = main ac5f23915. X5 = Design X with main ac5f23915 merged: the tree e232080fd2c29b94bd639d094cabe336bf7f6291 of the line's merge. M3 = main a819b10b2 and X3 = the combined check's merge bcca31e95 are the sides before the wobble's fix.

What causes what. The render scale controller sets a render size per draw from the measured frame time. A run that writes pictures stalls frames, so the scale is logged in a run without pictures on the card and played back per draw in the picture run. A played-back run gives the same pictures on the Mac as on the card. So the size log is the one input that differs between machines, and one size log on two codes is a true before and after.

Take A, the user's films: the cabin walk and the fair cabin lamp switch (the lamp's glow goes off with its light) under the dynamic render scale at the default frame time target of 16.67 ms on the card, 1920 x 1080 output, exposure 4.08 stops. One side-by-side video per film, M5 left and X5 right, by the video rules.

Take B, the hypothesis: X5's size log of each film played back on X3 and on X5 (and M5's log on M3 and M5 if cheap). Measures: the lengths of the histories (mean and share under 8 draws), the share of anchor-draws on which the region's drift test runs, the settle after off and on and after the walk's stop, the grain per frame, the boil without the fade split by size-change draws and draws of one size, the jamb crops' mean, the tracked edges and the level's kink at size changes.

Tools: the acceptance record's tools at 726bf338c by their paths (cloud.sh, cloudvideo.sh, cloudvideo.py, dynscale.py, playbuild.sh, sidevideo.py, wobble.py, boilnoise.py, scratch-region-log.patch, scratch-dynamic-scale.patch), and the combined check's levjump.py. A changed tool is copied into the taker's record, with the commit that it came from.

Limits: cloud money at most $2.00, only through tools/runpod/gpu_job.py. A rebuild in an existing probe folder at 22 GiB free or more, one at a time across the line. GPU lock at most about 20 minutes per take. A history-length read-back of more than about 40 scratch lines is not built: the taker reports and gives the other measures.

Out of scope: any change of the controller, of the region's drift test or of FSR; the cost block after a light change (the variants of t0-5tyj); the jamb leak's cause (t58-i3sn, the back-face line).

## Acceptance Criteria

- Two side-by-side videos (walk, fair lamp switch) of M5 and X5 under the dynamic render scale, each with the render scale and size shown per side, each with the motion measured by its maker and a list of what was not checked.
- One table per film with the measures of take B on X3 and X5 on the same size log.
- One plain statement: the wobble's fix helped Design X under a moving render size, or it did not, with the numbers that say so.
- By the user's bar, every fault that shows (grain, specks, boil, trail, flicker, bright jambs) is named per side.
- The record is committed, the raw frames are deleted, and the cloud spend is stated per job.


## Notes

**2026-11-22T19:45:41Z**

Taker: the combined check's lead, in its thread (thread:delegated-task:command%3Amcp%3Aa061e2e4-17a4-45d5-ac91-153bf5752d2c%3Adelegate-task%3Adesign-x-line-t0-gbgj-lead-1) and its worktree t3-52bfcd29-81249085ce80dd9f, branch t3/combined-renderer-check, after its separating run of t0-gbgj. Brief sent on 2026-11-22 about 14:46. The line's merge of main ac5f23915 is staged in the line's worktree (tree e232080fd2c29b94bd639d094cabe336bf7f6291); its commit waits for the acceptance lead's timing session, because the commit's hook builds and runs the Rendering tests. The orchestrator raised the line's cloud limit by $2.00 to $5.00 for these films.

**2026-11-22T19:49:12Z**

The films' plan from the combined check's lead, 2026-11-22 about 14:47, and my answers. Sent: one cloud job for the size logs (the acceptance lead's cloud.sh sizes of 726bf338c, copied into docs/research/t0-3vwx-dynamic-scale-films/tools/; target 16.6667 ms; M5 = ac5f23915 and X5; the walk and the switch; with the replay check; worst case about $0.14; output /private/tmp/t0-gbgj/cloud5). X5 goes to the cloud as the commit 4851ba7db, made with git commit-tree from the tree e232080f with the parents bcca31e95 and ac5f23915, on no branch; it is not the line's merge commit. Pictures are taken on the Mac from the card's size logs, because the cloud video tool returns 1280 x 720 at up to CRF 14 (a job returns at most about 6 MB). The history read-back is scratch-history-log.patch, 31 lines: with TERTIUS_X_HISTORY_LOG each draw reads the slow diffuse history that the draw before left (ReadImage(ScreenTraceSignals.Diffuse.Slow(0)), alpha = length), over that draw's render size, and logs the mean length, the share under 8 draws and the share at 0.

My answers (50 to 52): the lead's builds (x5h, m5h, x3h, m3h; about 45 MB each, into /private/tmp/t0-gbgj/bins/) wait for the end of the acceptance lead's timing session, because a build in a timed run can spoil a round; the lead may start when queue-variants.log shows the end of cost-xc-s2. Main's before and after (m3h) is taken for the lamp switch film only, as the control, last, and dropped if the disk or the time is short. My merge commit's test run starts after the lead's builds.

**2026-11-22T19:53:29Z**

The size logs of the films on the fixed code, from the card, 2026-11-22: the cloud job cost $0.0498 (241 s of execution); this take's spend is $0.0498 and the line's total is $1.79. At the 16.67 ms target, scale range / median / size changes: switch M5 0.770 to 1.000 / 0.992 / 100; switch X5 0.766 to 1.000 / 0.988 / 159; walk M5 0.521 to 1.000 / 1.000 / 78; walk X5 0.516 to 1.000 / 0.948 / 71. The replay check passed: X5's walk, played back on the card, gives the logged size on every draw. For comparison, before the layer and the two fixes the walk's scale stood still on the card (M 0.964, X 0.956), and the switch had 37 (M) and 101 (X) changes. The lead waits for the end of cost-xc-s2 before its builds.

**2026-11-22T19:57:22Z**

Correction, 2026-11-22: the comparison of the old and the new size logs in my note above is withdrawn. It set the old film window (draws 173 to the end) against the new whole log. I read the eight log files (old: /private/tmp/t0-5tyj/cloud/sizes/out/sizes; new: /private/tmp/t0-gbgj/cloud5/sizes/out/sizes). Over the same windows, scale range and size changes in draws 0 to 59 / 60 to 172 / 173 to the end:
- walk, main, old (c4c1f1f2f): 0.528 to 1.000, 57 / 0.922 to 0.964, 14 / 0.964, 0
- walk, main, new (ac5f23915): 0.521 to 1.000, 56 / 0.935 to 1.000, 21 / 1.000, 0
- walk, Design X, old (57b4045ae): 0.516 to 1.000, 58 / 0.911 to 0.956, 14 / 0.956, 0
- walk, Design X, new: 0.516 to 1.000, 57 / 0.902 to 0.948, 13 / 0.948, 0
- switch, main, old: 0.769 to 1.000, 54 / 0.970 to 1.000, 10 / 0.957 to 1.000, 37
- switch, main, new: 0.770 to 1.000, 50 / 0.969 to 1.000, 11 / 0.954 to 1.000, 39
- switch, Design X, old: 0.767 to 1.000, 51 / 0.967 to 0.993, 9 / 0.825 to 0.993, 101
- switch, Design X, new: 0.766 to 1.000, 54 / 0.967 to 0.989, 7 / 0.832 to 0.989, 97
The walk logs have 533 draws and the switch logs 493; all start at the scale 1.000 and hold 0.875 at draw 2. So the fall to 0.52 is in the first 60 draws of the run, before the film, on the old code as on the new, and the film's window did not change.

Three questions from the orchestrator, passed to the combined check's lead: (1) the two jobs' facts side by side (script and length, card and worker, target, the controller's smallest and largest scale, the probe build and how the scale was logged, the first draws); (2) the per-pass GPU timings summed into the fixed group and the group at the render size for the draws where the scale falls, beside a quiet stretch, with one more logging run on the card if needed, and a bug ticket by who owns the rising pass; (3) the GPU time of the draws right after a size change against draws of an unchanged size, for main and for Design X.

**2026-11-22T20:09:59Z**

The answers to the three size-log questions, from the combined check's lead, 2026-11-22. One more card job: the time and per-pass logs, job 89e20008, 335 s of execution, $0.0649; this take has spent $0.1147 and the line's total is $1.86. Two runs per scene and code under the dynamic scale, no pictures, with the 4-line scratch-pass-log.patch (one line per pass in the time log's loop); tool docs/research/t0-3vwx-dynamic-scale-films/tools/timeslog.py.

Question 1, comparable: yes. Old job ed738d9f (t0-5tyj) and new job f434f616: the same survey scripts (walk_script cabin-walk, 533 draws; switch_script cabin, 493 draws), the same endpoint ozf0ryg5dmsblx and image b93ab357 (the queue allows the RTX 2000 Ada only; the job does not print the card's name), target 16.6667 (the old job also 9, 10, 12), the controller FrameTimeTarget(target, 0.5, 1) of scratch-dynamic-scale.patch, the scale logged as the controller's own value per draw from draw 0, the same order of runs. In both jobs the walk is the first run of each binary.

Question 2, measured: the passes at the render size rise at the start of every run. (a) Every run that is not the binary's first: draws 0 and 1 at 1920 x 1080 cost 26.3 to 26.5 ms of GPU span, of which 23.1 to 24.5 ms at the render size (diffuse_rays 5.77, filter_indirect_diffuse 5.35, reflection_rays 4.85 ms), 0.16 ms before the render size (1.66 on draw 0) and 1.4 to 1.7 ms after it. diffuse_rays falls from 3.40 ms at draw 7 to 1.32 ms at draw 8. The scale falls to 0.88 at draw 3 and 0.771 at draw 9, and climbs back to 0.92 by draw 40 and 0.99 by draw 100. The camera stands still there. Main and X5 behave the same. This is ter-4iic: every history is younger than 8 draws and traces its own ray. (b) The first run of each binary on the worker: draw 0 costs 230.5 ms (M5) and 238.7 ms (X5), of which 211 to 220 ms at the render size (diffuse_rays 60.0, reflection_rays 50.5, direct_light 21.5) and 16.9 ms before it; the scale then falls to 0.528 at draw 11 and reaches 0.92 after about 60 draws. The second run of the same binary costs 26.3 ms at draw 0. So every walk log falls to about 0.52 because the walk ran first, and every switch log to 0.77. Quiet stretch, draws 100 to 170: about 0.175 ms before, 12.4 to 12.9 ms at the render size, 1.6 to 2.0 ms after, 2.3 to 2.4 ms output (with the headless capture of about 2.3 ms, ter-fw73).

Tickets by the lead under ter-8hyd, linked to ter-4iic: t58-np60 (bug, tag back-face-line): at every start the render scale falls to about 0.77 within 9 draws and needs about 100 draws to climb back. t58-phgr (bug): on the first run of a probe build on a fresh card draw 0 costs 230 to 239 ms and the scale falls to 0.53; likely cause the driver's shader cache per binary, not shown; not checked whether a game pays it once per install or once per launch.

Question 3, measured: no. In the switch film's window, draws 1 to 3 after a size change against draws of a size unchanged for 6 or more draws at the same render size (3 sizes, 15 to 18 draws against 157 to 226): GPU span main +0.042 and -0.036 ms in two runs, X5 -0.010 and +0.025 ms, on a span of about 16.8 ms. A size change does not make the next draws dearer.

Take A: the switch's pictures on M5 ended at 15:09 (320 frames, played-back sizes equal the card's log on every draw); X5 renders. The four builds x5h, m5h, x3h, m3h were built from 15:02:48 to 15:05:49; the disk fell from 26.9 to 26.2 GiB.

**2026-11-22T20:30:26Z**

The first film on the fixed code, the fair lamp switch under the dynamic render scale, from the combined check's lead, 2026-11-22, committed as 7cf1f365e on t3/combined-renderer-check. Video: docs/research/t0-3vwx-dynamic-scale-films/pics/switch-M5-X5.mp4 in the lead's worktree t3-52bfcd29-81249085ce80dd9f (11.1 MB, 3840 x 1080, 320 frames at 60 fps, H.264 yuv420p, CRF 12; M5 = main ac5f23915 left, X5 = Design X without the cap with that main right; each side plays its own card log at 16.67 ms; render scale and size per draw on screen, orange on a changed draw). Frame sheet: pics/switch-frames-M5-X5.png. I checked the format with ffprobe and looked at the frame sheet: both sides look alike, the jambs are grey at all five moments, and X5 is darker 60 draws after off (it settles faster). The pictures are the Mac's, played back from the card's logs; played-back sizes equal the logs on every draw.

Measured: in the video's draws M5's scale runs 0.954 to 1.000 with 39 size changes, X5's 0.832 to 0.989 with 97. Jambs: largest jamb mean 0.0440 (M5) and 0.0436 (X5), none above 0.08. Both sides show the same small faults at the near left window's sill, 60 and 195 draws after off: a few bright dots on the sill's lower edge and a soft glow on the inner jamb, a little stronger on X5. Edges: the window edges' distance ranges 1.69 px of 1920 on M5 (rms 0.265) and 2.16 px on X5 (rms 0.312); correlation with the rounded aspect +0.048 and -0.010; floor 0.062 and 0.025 px. Decoded video against source frames: the center region's change agrees within 0.3 %. Not checked: whether an eye finds the 0.3 px edge motion; other edges; faults beyond five moments and three decoded frames.

Measured, history lengths on this film (the read-back of what the draw before left), share of pixels under 8 draws on one-size draws / changed draws: 0 to 60 draws after off X5 0.12 / 0.20, M5 0.05 / 0.05; 0 to 100 draws after on X5 0.05 / 0.18, M5 0.02 / 0.02. Mean length X5 about 21 to 29 draws, M5 about 26 to 30. X5's region test runs on 11.7 % of the anchor-draws in the 100 draws after off and 12.1 % after on (before the fix: 12 % and 7 % on the card's two targets). The settle measures follow. The switch's frames are deleted (1,110 MiB); the disk shows 21 to 23 GiB.

**2026-11-22T20:37:13Z**

The second film on the fixed code, the cabin walk under the dynamic render scale, from the combined check's lead, 2026-11-22, committed as 46a7a4af0 (the record names X5 as the line's merge 710834cc0 since f66fd4d86). Video: docs/research/t0-3vwx-dynamic-scale-films/pics/walk-M5-X5.mp4 in the lead's worktree (6.1 MB, 3840 x 1080, 260 frames at 60 fps, H.264 yuv420p, CRF 12, which I checked with ffprobe; M5 = main ac5f23915 left, X5 = Design X without the cap right, each on its own card log; the walk's last 60 draws and 200 draws after the stop). Frame sheet: pics/walk-frames-M5-X5.png. Played-back sizes equal the card's logs on every draw.

Measured: neither scale moves in the video's draws: M5 stays at 1.000 and X5 at 0.948, no size change. The trail after the stop, center region against the last frame, at 0 / 20 / 40 draws after the stop: M5 +5.5 / +3.7 / +2.5 %, X5 +5.9 / +4.6 / +3.6 %; decoded video against source within 0.31 %. The lead's description: both sides look alike and clean, with soft, low-contrast clouds on the floor under the near lamp on both sides; the grain is about equal. Not checked: the trail by eye at normal speed; the edges, because the camera moves until the stop. The walk's frames are deleted (674 MiB).

Result: in the walk's window the wobble's fix has nothing to act on, on either code. X5 fades back more slowly than main, as Design X did before (+5.72 / +4.56 / +3.56 % on the old code).

My answers (56, 57): the walk's run on the code before the fix is dropped, because no size changes in the window. In its place the lead plays X5's switch log back with the lamps on through the whole run: 97 size changes at a still light and a still camera, which separates the change of size from the change of light as the cause of the wobble's residual. Take B's queue: the switch before the fix on the same log, the still-light run, then main's switch before the fix as the control. Each waits for 22 GiB free; the disk shows 20 GiB.
