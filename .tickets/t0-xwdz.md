---
id: t0-xwdz
status: in_progress
deps: []
links: [ter-bcwr, ter-4iic, t58-np60, t0-y88b]
created: 2026-11-23T12:08:20Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, temporal]
---
# After a reset, 23 to 50 % of the pixels still trace their own reflection ray in draw 8, and the tail lasts to about draw 12

Found by the lead of the announced dip of the render scale (ter-bcwr) on 2026-11-23, in the own-ray logs of the catch-up ray experiment (/private/tmp/t0-gbgj/gumm-mac/{cut,start}-today/ownrays-none.txt, 1280 x 720, today's code). Reflection / diffuse own rays per draw after a reset: draws 0 to 7 about 678,000 / 678,000 (every pixel); draw 8: 154,400 / 2,306 after a camera cut and 335,822 / 32,997 after a start; draw 9: 43,798 / 1,991 (cut); draw 12: 15,711 / 1,988 (cut). So the bounce light's own rays end at draw 8 as designed, and the reflections' do not: 23 % (cut) to 50 % (start) of the pixels still trace their own reflection ray in draw 8. On the card draw 8 costs 1.9 to 3.6 ms more than draw 9 for it (15.9 to 16.5 ms per unit of area against 12.8 to 13.4). At a start, draw 8 is the first ordinary measurement of the render scale's controller, and it alone lowers the ordinary request to 0.92 to 0.96, which takes 28 to 39 draws to return (part of the start's own dip, t58-np60).

The cause is inferred from the shader code and is not logged: the anti-lag of accumulate_spatial.comp acts once a history is longer than the fast cap (4) and cuts the length to max(fastCap, L (1 - lag / antiLagShare)). The reflections' box takes no retained widening, so in draws 5 to 7 a young and noisy reflection history moves the clamp and is cut back to 4 at many pixels. Those pixels read under FullRateFrames in draw 8 and some for a few draws more.

## Design

Step 1, measure and build nothing: log the reflections' history length per draw 4 to 12 after a cut and after a start (the share of pixels under FullRateFrames, and the share that the anti-lag shortened in that draw), to confirm or refute the inferred cause. Step 2, only with the cause confirmed, write the design as a note here. Two directions to weigh then, neither chosen: the anti-lag does not shorten a reflection history that is still young (its excursions are its own noise, not a change), or the plan's rule for an own reflection ray counts the draws since the reset and not the stored length. Each changes what the first draws after a reset look like, so the picture of draws 0 to 20 decides.

## Acceptance Criteria

The record holds the per-draw log and names the cause with its evidence. If a change follows: after a cut and after a start, the reflections' own rays in draw 8 fall to the level of draw 12, the picture of the first 20 draws is not worse than today's in a side-by-side video, and at a start the render scale's ordinary request is not lowered by draw 8.


## Notes

**2026-11-23T14:12:05Z**

2026-11-23, 09:12 New York time, line orchestrator of the Design X line. Step 1 starts on the line's free slot (answer 100, reported to the orchestrator): the lead of the render scale's dip, in its worktree t3-52bfcd29-81249085ce80dd9f, branch t0-xwdz-reflection-tail from main 4bb8489b3. It measures and builds nothing that merges: a scratch probe logs the reflections' history length in draws 4 to 12 after a camera cut and after a start, on the Mac, and the record goes to docs/research/t0-xwdz-reflection-tail. Money: 0.

**2026-11-23T14:13:53Z**

2026-11-23, 09:13 New York time, line orchestrator. Step 1 did not start: no brief went out. The line's free slot goes to the shadow softness ticket t1-af5w, which the orchestrator gave to the line at priority 1 on the user's word. This ticket is open again and waits for a slot.

**2026-11-23T16:52:05Z**

2026-11-23, 11:52 New York time, line orchestrator. Step 1 starts: the lead of the region's cap (worktree t3-c3911144) takes it after its record of the cap, on a branch t0-xwdz-reflection-tail from the line's head. It logs the reflections' history length in draws 4 to 12 after a camera cut and after a start: the share of pixels under FullRateFrames and the share that the anti-lag shortened in each draw. On the Mac, money 0, a scratch probe that does not merge. It brings a verdict on the inferred cause and no design.

**2026-11-23T17:42:18Z**

2026-11-23, 12:42 New York time, line orchestrator of the Design X line: step 1's interim result (the lead's record on the branch t0-xwdz-reflection-tail, 40fff9335). The inferred cause is partly confirmed. The anti-lag cuts young reflection histories in draws 4 to 7 (5 to 6 % of the cabin's surface pixels per draw, 6 to 12 % of the bay's), and the 15.6 % (cabin) and 21.2 % (bay) that are behind schedule at draw 7 trace their own ray in draw 8. Most cuts are small and do not reach the fast cap of 4. A second cause: 9.1 % of the cabin's pixels hold a length in [7.999, 8) at draw 7 and fail the plan's test historyLengthOf >= FullRateFrames; they give about 36 % of draw 8's 176,403 own rays. They were under 8 before draw 7's anti-lag, so they come from cuts under 0.001 in draws 4 to 6 or from rounding. The tail after draw 9: the anti-lag cuts 8 to 12 % of the pixels per draw from a length of 8 or more, and 17 to 38 % of those cuts end under 8. The v3 log, which splits the tiny cuts from the rounding, runs now. From the orchestrator: the near-8 lengths look like a rounding defect; if the log shows rounding, the fix is small and the line orchestrator settles it.

**2026-11-23T17:35:54Z**

Step 1, the v2 log (record docs/research/t0-xwdz-reflection-tail/, line head c768e1ed8, frozen fair test, 1280 x 720). In a frozen world the start and the cut back give the same log draw for draw. Draw 8's own reflection rays: 19.5 % of the cabin's surface pixels (176,403) and 25.3 % of the bay's. Verdict: partly confirmed. The anti-lag cuts 5 to 12 % of the young histories per draw in draws 4 to 7, so 15.6 % (cabin) and 21.2 % (bay) are behind schedule at draw 7. Most cuts are partial and do not reach the fast cap of 4. Not in the inferred cause: 9.1 % of the cabin's lengths end draw 7 in [7.999, 8) and fail the plan's >= 8 test in the 32-bit history, about a third of draw 8's own rays. The tail after draw 9 comes from cuts of histories of 8 or more (8 to 12 % of the pixels per draw, 17 to 38 % of them ending under 8 in the cabin). A length test at 7.5 saves 36 to 70 % of draw 8's own rays in the cabin. Open: the v3 log (scratch-tail-log.patch, written, applies, not built because the disk was under 22 GiB) splits the near-8 lengths into tiny cuts and rounding, counts the 7.5 split, and gives the histogram of the cuts' starting lengths.

**2026-11-23T17:52:08Z**

Step 1 finished with the v3 and v4 logs (record docs/research/t0-xwdz-reflection-tail/). Draw 8's own reflection rays by the own texel's length after draw 7: cabin 37.4 % in [7.99, 8), 13.8 % in [7.5, 7.99), 43.5 % under 7.5, 3.4 % a weighted tap under 8, 1.9 % the lobe fit (bay: 34.1, 7.5, 54.5, 2.6, 1.3 %). The near-8 lengths are a rounding defect of the reprojected place: in a still frame the place lies a float rounding off its own texel, so the other three taps take weights of 1e-7 to 1e-3, and next to a neighbor that a real cut shortened the length falls less than 1e-3 under its schedule (97.8 % of the near-below lengths in the cabin, 98.7 % in the bay). The mean, the blend and the 32-bit store do not round, tiny cuts give 1.4 % at most. The tail's cuts in draws 9 to 12 mostly start from on-schedule histories and end at every length from 4 up, 20 to 64 % under 8. In the bay about 15,000 pixels per draw are cut from [5, 6) back to the fast cap. First change proposed: make a still frame's reprojection read its own texel with weight 1 (37.4 % and 34.1 % of draw 8's own rays). The young-history cuts and the tail's cuts are for step 2.

**2026-11-23T17:54:22Z**

2026-11-23, 12:54 New York time, line orchestrator of the Design X line: step 1 is finished and its record is on the line (docs/research/t0-xwdz-reflection-tail, merge 8b8ef76d0). Draw 8's own reflection rays split three ways by the own texel's length after draw 7 (cabin 176,403 / bay 224,864): 37.4 % / 34.1 % from lengths in [7.99, 8), which a rounding of a still frame's reprojected place makes; 13.8 % / 7.5 % from [7.5, 7.99); 43.5 % / 54.5 % from under 7.5, the anti-lag's partial cuts of draws 4 to 7. The tail after draw 9 is the anti-lag's cuts of histories on schedule, 8 to 12 % of the pixels per draw, of which 45 to 20 % (cabin) and 64 to 40 % (bay) end under 8; in the bay about 15,000 pixels per draw go from [5, 6) back to [4, 5) and never reach 8. My decision (answer 103, as the orchestrator allowed for a rounding defect): the rounding is its own bug, t0-y88b, fixed at the one place that makes a reprojection's weights, and it is built first. The design of the rest (the partial cuts and the tail) follows in this ticket after that fix, because the fix changes the counts that the design starts from.
