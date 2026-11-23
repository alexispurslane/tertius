---
id: t0-gumm
status: in_progress
deps: []
links: [ter-4iic, t0-o7gf, t0-zmjx, t58-np60, t0-qvm1, ter-6fka, ter-8pco]
created: 2026-11-22T21:31:16Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Spread the catch-up rays of a young history over more draws: one share function of the history's length in place of the cliff at 8 draws (experiment)

An experiment, scratch code only. It does not gate the merge of Design X (t0-iido).

The user's words, 2026-11-22, after the video and the card's chart of t0-zmjx: "the issue is that the 8 frame reset basically just doesn't allow 'catch up rays', that's why there's no spike. i think catch up rays are important after a reset of the history. maybe spreading them out more somehow?"

The orchestrator's reading: the user is right about the cause. With a restart at 8 draws no history falls under FullRateFrames, so no pixel buys a ray of its own. The own rays stay for histories that are truly empty (a start of a run, a camera cut, an uncovered surface), and there they make the cliff of ter-4iic: 26.3 to 26.5 ms at draws 0 and 1 on the card, the scale to 0.771, about 100 draws back (t58-np60). So "spread the catch-up rays" is one remedy for the light change and for the start. It is a bound on the own rays, a piece of the cost model and not a trust rule, so the hold on new trust rules (the ray census ter-8pco) does not cover it. If it wins, the restart after a light change returns to 4 draws in a later ticket. The structural step ter-6fka may later make own rays unnecessary; that is weeks away and not decided, so this experiment stands on its own.

Terms. A block is 2 x 2 pixels. In each draw one pixel of a block, the chosen pixel, traces the block's ray of a kind, and its turn moves with the draw index (chosenPixelOf in Shaders/screen_trace.glsl). An own ray is a ray that another pixel of the block traces for itself. A catch-up ray is an own ray that a pixel traces because its history is young, not because the block's ray does not fit it. The history's length L is the slow history's alpha, in draws; it grows by 1 per draw whether the pixel traced an own ray or took the block's.

Today (Shaders/screen_trace_plan.comp): "if (historyLengthOf(r, kind) >= FullRateFrames && fits(p, c, kind)) continue;", else the pixel gets an own ray. So the catch-up rate is a cliff: 1 own ray per pixel per draw while L < 8, then none. From an empty history that is 8 catch-up rays in 8 draws. After a light change on Design X it is 11: light evidence holds L at 4 for the 7 draws in which the light age is recent, then L grows from 4 to 8.

## Design

The model. Replace the cliff by one function of the history's length: the share of draws on which a young pixel traces a catch-up ray. It falls as the history grows and reaches zero later than 8 draws. The count of catch-up rays of one restart is the sum of the shares over its draws, so the same rays can arrive over more draws at a lower peak. One function replaces one constant in the plan.

Who owns what. Nothing new is stored. The accumulation owns L and is the one place that changes it. The ray plan owns the decision "this pixel traces an own ray in this draw" and reads the last draw's L, as now. The share function and the turn rule live in the plan, because only the plan reads them. The trace, the resolve and the accumulation do not change: they follow the plan's bits and the own-ray lists.

Signatures, in Shaders/screen_trace_plan.comp:

    // The share of draws, 0 to 1, on which a pixel whose history of a kind holds `historyLength` draws traces a ray of
    // its own to catch up. 1 is today's rate under FullRateFrames. `cap` is the history's cap at the pixel, in draws.
    float catchUpShare(float historyLength, float cap)

    // Whether the pixel takes its turn of the catch-up rays of the kind in this draw, for a share of 0 to 1. A share of 1
    // is always true and a share of 0 never.
    bool catchUpTurn(ivec2 pixel, uint kind, float share)

    // in main, in place of the test against FullRateFrames:
    if (fits(p, c, kind) && !catchUpTurn(pixel, kind, catchUpShare(historyLengthOf(r, kind), capOf(p, kind))))
        continue;

The share's shape, three parameters: PeakShare (0 to 1), EndFrames (draws, above 0) and Fall (0 = a box, 1 = a straight fall):

    share = L >= End ? 0 : PeakShare * pow(1 - L / End, Fall)        with End = min(EndFrames, cap)

(1, 8, 0) is today's cliff, and a build with those values must give today's own-ray count in every draw: that is the experiment's first check. The parameters are read at run time (an environment variable into the plan's parameters, or a specialization constant), so that the search by halving needs no rebuild and one cloud job runs many shapes. The lead says how.

Invariant: a settled history traces no catch-up ray. So the share is 0 at the kind's cap at the pixel at the latest (End = min(EndFrames, cap)), and a pixel whose cap is under FullRateFrames keeps today's own ray. Without this, a history capped under EndFrames would trace a share of own rays for ever and raise the settled cost. Check: the settled own-ray count per draw equals today's code in each scene.

The turn rule and the reason for its order. phase = offset(pixel, kind) + drawIndex * 2654435769 in 32-bit unsigned arithmetic (2654435769 is 2^32 divided by the golden ratio); the pixel takes its turn when phase < share * 2^32. offset is the pixel's value of the blue-noise mask's first frame for the kind, widened to 32 bits (or drawOf(pixel, 0, a constant of the kind) if the mask's 8 bits are too coarse for small shares; the lead says which it took and why).
- In time: the golden-ratio step gives each pixel its turns at nearly even gaps of 1 / share draws, for every share. So a young history gains its own samples steadily, and no pixel waits long while its neighbor traces twice.
- In space: a blue-noise offset gives, in each draw, tracing pixels that are spread evenly with no clumps. So each draw costs the same number of rays, and each young pixel has a neighbor with a fresh own sample for the history fix of the accumulation.
- It depends on the pixel's place, the kind and the draw index only, so one machine gives the same plan in every run.
- The fault of t2-8foa must not come back: the block ray's turn flips the chosen pixel's column parity with the draw index's parity, which FSR's jitter also follows. The golden-ratio step is not locked to the draw's parity, and the offset has no column structure. Check by count: over the catch-up window, the plan's own-ray bits by (column parity of the pixel, parity of the draw index) are four counts within 2 % of each other.

The experiment's tree: Design X on the line's merge 710834cc0 with light evidence restarting at 4 draws, which is that commit as it is (the cap of t0-zmjx is not in it). The scratch patch is kept in the record like the earlier ones; it does not go into the line. Region evidence keeps its cap at FullRateFrames, 8 draws, in the accumulation. Both kinds, the bounce light and the reflections, take the same shape; the lead reports if the reflections need their own.

Steps.
1. Build the share function and the turn rule as above. Check (1, 8, 0) against today's own-ray counts.
2. Shapes to try at least: today's total of own rays spread over 4 times the draws (PeakShare 0.25, EndFrames 32, Fall 0), and half of today's total spread the same way (0.125, 32, 0). Then search PeakShare by halving, with EndFrames and Fall as the second and third search where they matter.
3. The arithmetic to beat. The controller's band is 3 % of its budget, about 0.45 ms at the 16.67 ms target. Today's own rays add about 7 ms at their peak on the card after a light change and about 9 ms at a start. Main's own block of about 2.5 ms moved the scale by 0.041. Find the largest PeakShare whose block does not move the scale on the card's log by more than the idle span's changes. If no share passes that with a useful catch-up, say so, and report the share that moves the scale no more than main does.
4. Measure each kept shape on three events, on the card's scale log at the 16.67 ms target and on the Mac at the fixed scale 2/3: the fair lamp switch of the cabin, the start of a run (from draw 0), and a camera cut. For each: the GPU span per draw, the scale's largest fall and its draws back, the settle, the boil without the fade in the first 60 draws, and the grain of the first 16 draws after the event against today's code. Pitfall of ter-4iic: a headless still after a camera switch runs 32 untimed warm-up draws, so the view deck-cuts does not measure a cut. Use a film in which the camera switches between two film frames (the six-frame cuts film, if its histories do restart at the cut), and prove with the history-length log (scratch-history-log.patch of t0-3vwx) that the histories are empty in the draw after the cut.
5. One video per event with three panes at one exposure: the cap as built (t0-zmjx), today's cliff (Design X with the restart at 4 draws), and the best spread shape. Under the dynamic scale, each code on its own scale log from the card, where the event's point is the scale: the user judges under the dynamic scale, and the cliff's price is the fallen scale. The video rules of the line hold (60 frames a second, one draw per frame, labels, a draw counter, the render scale shown per pane, H.264 yuv420p at CRF 12 or lower, motion measured, what was not checked).
6. The two-design table: the cap as built against the best spread shape with the restart at 4 draws, with the orchestrator's rule applied (no cost peak that moves the scale; then the faster settle unless the boil in the first 60 draws is higher by more than the spread between runs; then the smaller reach and less code) and a recommendation. Here the spread between runs is needed: runs of one code with the event moved by one and two draws.

Limits. Scratch code, so no tests first; a production build is a later ticket with red tests first, and it then says what region evidence's cap at FullRateFrames means once the cliff is gone. Cloud money: at most $1.50 of the line's $5.00, through tools/runpod/gpu_job.py only. The GPU lock at most about 20 minutes per take. Disk: 22 GiB free to start a build or a take, stop under 20. No timing run at a Mac load that spoils it; give the load beside each number that depends on it.

Out of scope: a change of the accumulation, of FullRateFrames' use by region evidence, of RecentLightAge, of the block's turn (t2-8foa), of the rays that do not fit; the design P of ter-6fka; any production code.

## Acceptance Criteria

- A build with the shape (1, 8, 0) gives today's own-ray count in every draw of the lamp switch's film.
- The settled own-ray count per draw of each kept shape equals today's code in each measured scene.
- The four parity counts of the catch-up rays are within 2 % of each other, or the report says why not and what follows.
- The report names the largest PeakShare that does not move the scale on the card's log by more than the idle span's changes, for the lamp switch, the start and the cut, or says that none exists and names the share that moves it no more than main does.
- For each kept shape and each event the table holds: GPU span per draw (peak, draws above settled), the scale's largest fall and its draws back, the settle, the boil without the fade, and the grain of the first 16 draws, beside today's cliff and the cap.
- One three-pane video per event exists, with its motion measured and its limits stated.
- The two-design table with the rule applied and a recommendation is in the report. The scratch patch and the logs are committed in the record.


## Notes

**2026-11-22T21:31:26Z**

Written by the line orchestrator on 2026-11-22 from the orchestrator's order. Lead: the acceptance lead (thread design-x-line-t0-xlcu-lead-1, worktree t3-c3911144), after the cap's build (t0-zmjx) and tc-8142, or beside them where it waits on nothing. Links still to make when those tickets reach the line's branch: ter-6fka (the structural step), ter-8pco (the ray census) and t58-np60 (the scale's fall at every start, in the combined check's worktree). Facts for the design: the diffuse signal's cap is 32 draws at every roughness (SignalParameters.cs), so EndFrames = 32 ends at the cap for the bounce light; the reflections' cap follows the roughness (capAt in accumulate.glsl), and the plan does not have the accumulation's parameters today, so the scratch may carry the caps as constants and the lead says so. My design answers, which the orchestrator can reverse: the turn rule (a golden-ratio step in time on a blue-noise offset in space), the invariant that a settled history traces no catch-up ray, the check of (1, 8, 0) against today's counts, and the limit of 1.50 dollars of cloud money.

**2026-11-22T21:39:00Z**

Answer 73 of the line orchestrator, 2026-11-22: the experiment moves from the acceptance lead's queue to the combined check's lead (thread thread:delegated-task:command%3Amcp%3Aa061e2e4-17a4-45d5-ac91-153bf5752d2c%3Adelegate-task%3Adesign-x-line-t0-gbgj-lead-1, request design-x-line-t0-gbgj-lead-1, worktree t3-52bfcd29-81249085ce80dd9f, branch t3/combined-renderer-check), as a continuation sent about 16:50. Reason: its slot became free with the close of t0-gbgj, and it holds the build of the experiment's tree (x5h, the line's merge 710834cc0), the card's logging jobs, the history-length log and the video tools. It runs the acceptance lead's cost and boil tools by path. Order of its work: the experiment now; the final films of t0-3vwx first when the cap's commit exists; then the experiment's three events, videos, table and one reviewer; one final JSON for t0-3vwx and t0-gumm. Links to t58-np60 are made; ter-6fka and ter-8pco are not on the line's branch yet.

**2026-11-22T22:10:27Z**

Early result 1 from the combined check's lead, 2026-11-22, commit 91b122d3a on t3/combined-renderer-check, record docs/research/t0-gumm-catch-up-spread (chart pics/ownrays-switch-23.png: diffuse own rays per draw of the switch film at the constant scale 2/3, at the start, after off and after on).

Check 1 passes: the experiment's build with no shape and with the shape (1, 8, 0) gives an own-ray log equal to today's code byte for byte in all 492 draws.

Two changes of the design, accepted (my answers 74 and 75):
1. The share of the length alone breaks the invariant. In the settled cabin the anti-lag holds about 17 % of the diffuse histories between 8 and 32 draws for good; the shape (1, E, 0) then gives 33,844 / 65,315 / 130,638 / 163,080 settled diffuse own rays per draw for E = 12 / 16 / 24 / 32, against today's 2,164. So the share acts only on a history that restarted less than End draws ago, by three clocks: draws since a reset (drawIndex), draws since the pixel's own exact light changed, and the block's youngest light code of the last draw's samples (the read of "own rays wait for the evidence"). Every other history keeps today's rule. Without the block's clock about half of today's peak stayed after a lamp switch.
2. The offset takes the blue-noise byte as its high 8 bits and a pcg3d hash of the pixel as its low 24. The caps are scratch constants (diffuse 32; reflections 4 to 32 by roughness).

Numbers: today peaks at 678k diffuse own rays per draw for 8 to 12 draws; (0.25, 32, 0) is a flat 176k for 32 draws; (0.125, 32, 0) about 92k. Totals of diffuse catch-up rays over 60 draws, today / (0.25, 32, 0) / (0.125, 32, 0): start 5.41 M / 5.41 M / 2.71 M; off 6.79 M / 5.25 M / 2.64 M; on 6.37 M / 5.22 M / 2.63 M. Settled diffuse counts equal today's; reflection counts differ by at most 15 per draw.

Parity: within each draw parity, even and odd columns differ by at most 0.33 %, so the fault of t2-8foa is absent; that is the number that counts, and it passes. The four (column, draw) counts lie within 2.6 % of a quarter because the odd draws of a light-change window take about 2.3 % more catch-up rays; cause not found, not hunted now.

Ordered in addition: one map of the settled diffuse history length of the cabin still with the share of pixels per band, for the orchestrator's review of the approach, and a statement of what a production build would need for the restart's clock.

**2026-11-22T22:30:06Z**

Early result 2 from the combined check's lead, 2026-11-22, commits 24a46634d and ddd14a36c on t3/combined-renderer-check (record docs/research/t0-gumm-catch-up-spread; pics/card-scale-shapes1.png and pics/history-map-cabin.png).

The card, job 1 (RTX 2000 Ada, 16.67 ms, 1920 x 1080, $0.0542): scale changes and largest fall of the render scale in the window after each event, start / off / on / cut; the idle span has 0 changes for every code.
- Today (1, 8, 0): 57, 0.232 / 54, 0.144 / 53, 0.150 / 49, 0.113.
- (0.25, 32, 0): 45, 0.127 / 32, 0.045 / 33, 0.052 / 31, 0.053.
- (0.125, 32, 0): 42, 0.102 / 14, 0.020 / 23, 0.026 / 16, 0.029.
- (0.0625, 32, 0): 39, 0.091 / 3, 0.005 / 5, 0.009 / 3, 0.014.
- The cap: 57, 0.234 / 0, 0 / 0, 0 / 50, 0.127.
In no window with a fall does the scale come back within 60 draws. The search goes on to 1/32 and 1/64; (0, 32, 0) will measure the part of the start's fall that is not catch-up rays. Job 2 waits for the daily cloud limit's reset at 19:00 local time (all agents: $2.88 of $3.00).

The history map of the settled cabin still (Design X, lamps on, draw 190, 2/3), share of pixels by the length of the bounce light's history: under 8 draws 1.9 % (the window openings), 8 to 16 9.7 %, 16 to 24 6.6 %, 24 to 31.5 4.5 %, 31.5 or more 77.4 %. Correction of early result 1: the 20.8 % between 8 and 31.5 draws are whole rectangles of the drift test's region size, held short by the false fires of the region test (t0-qvm1; 1 to 11 of 238 anchors fire in each of the draws 100 to 131), not by the anti-lag. The restart's clock by three reads still stands: the length alone cannot tell these from a restart.

**2026-11-22T22:30:58Z**

The orchestrator's answers to early result 2, 2026-11-22: the daily cloud limit stays, job 2 waits for 19:00; the start is reported as two numbers after the shape (0, 32, 0); the final table compares three codes: A, today's cliff with the cap (what merges); B, the spread with the restart at 4 draws; C, the spread together with the cap (my reading: with the spread's catch-up rays also after a light change, by the three clocks; C' with the light clocks off only if one parameter does it).

**2026-11-22T22:51:20Z**

Early result 3 from the combined check's lead, 2026-11-22, commits e4e1dd3f0 and 0ad907f2f (pics/card-scale-shapes2.png). Card job 2, $0.0846, the ticket's spend $0.1388. Scale changes and largest fall, start / off / on / cut:
- Today: 0.232 / 54, 0.154 / 53, 0.151 / 56, 0.140.
- A, the cap: 0.231 / 0 / 0 / 58, 0.140.
- (0, 32, 0), no catch-up ray: 0.070 / 0 / 0 / rises only.
- B32, (0.03125, 32, 0): 0.084 / 0 / 1, 0.005 / rises only.
- B16, (0.0625, 32, 0), second run: 0.092 / 8, 0.013 / 9, 0.008 / 4, 0.003.
- C32, the cap with B32's shape: 0.086 / 0 / 1, 0.004 / rises only.
- C' at 1/16 (the cap, light clocks off): 0.093 / 0 / 0 / 5, 0.007.
The search's answer is a peak share of 1/32 (1/64 gives the same): the card's own noise reaches 3 changes in an idle span, B16's two runs differ (3 and 8 changes after off), so B32's one change lies inside the noise and 1/16 moves the scale in both runs. EndFrames 16 and Fall 1 at 1/16 do not beat the smaller peak with the box.

The start in two parts (largest fall, scale lost over 60 draws): without catch-up rays 0.070, 3.29; today's catch-up rays on top 0.162, 4.10; B32's catch-up rays on top 0.014, 0.64.

A new film for the camera cut: person camera, one frame on the chase camera, the person camera again; at draw 134 the mean history length falls from 25.6 to 0.98 draws. The old cut film (bright exterior, then the cabin) stayed saturated white for over 100 draws; the lead files that as a bug.

My reading: 1/32 for 32 draws is 1 catch-up ray per restarted pixel, against 8 to 11 today and none with the cap; this card allows no more. The Mac's measures must show whether that one ray buys anything against the cap alone. The share is a cost budget; a production form could take it from the controller's headroom (noted, not built).

**2026-11-23T01:51:07Z**

The card's first-run fact (t1-80jd), 2026-11-22: no measured run of the two card jobs was first in its job, because cardrun.sh discards one run of every build at the start. Ordered (my answer 84): one more job with two runs each of today's cliff, the cap, and 1/16 and 1/32 with and without the cap, on the lamp switch and the cut, because the difference between 1/16 and 1/32 is a few size steps and '1/32 holds' rests on one run. If the two runs of 1/32 differ as much as 1/16 differs from 1/32, the table names the range and not one value. The spread's decision waits for this job.

**2026-11-23T02:05:08Z**

Early result 5 from the combined check's lead, 2026-11-22 (pics/card-scale-shapes3-zoom.png). Card job 3 (shapes3, $0.0898; one discarded run of every build, then each code once, then each code again); the ticket's cloud spend $0.2286. Scale changes and largest fall, run 1 / run 2, off; on; cut:
- Today (1, 8, 0): 54, 0.151 / 53, 0.156; 53, 0.154 / 53, 0.154; 56, 0.139 / 56, 0.144.
- A, the cap: 0 / 0; 0 / 1, 0.004; 56, 0.141 / 56, 0.140.
- B16: 2, 0.009 / 8, 0.013; 10, 0.009 / 11, 0.013; 4, 0.003 / 8, 0.006.
- B32: 0 / 0; 4, 0.013 / 1, 0.004; 3, rises only / 3, rises only.
- C16: 3, 0.010 / 1, 0.005; 12, 0.013 / 8, 0.009; 10, 0.011 / 8, 0.006.
- C32: 0 / 0; 1, 0.004 / 0; 4, 0.002 / 3, rises only.
One change of about 0.004 is the card's noise (the cap's run 2 after on; C32's run 1 in the idle span). The card tells 1/16 from 1/32: every run of 1/16 moves the scale after off and no run of 1/32 does; the one overlap is B32's run 1 after on. So 1/32 stands; B32 sits at the limit and C32 stays inside the noise.

The films' size jobs (t0-3vwx, commit 6ae9b670c) had no warm-up; both ran walk m (the job's first run), walk x, switch m, switch x. Main's walk rested at 1.000 in take A and 0.956 in the final take, first in both, so the order alone does not explain it. The verdicts compare spans inside one run and stand.

The lead's three-pane videos (pics/switch-three.mp4, start-three.mp4, cut-three.mp4, commit a56713d0b) are 2880 x 540 at 30 fps by my ffprobe and do not meet the line's video rules. Sent back: 60 fps, one draw per frame, 1280 x 720 per pane at least, each code on its own card scale log, panes A | today's cliff | the best spread shape. Not passed up. The disk has 21 GiB free, under the floor of 22; the region take waits.

**2026-11-23T02:49:50Z**

The three-code table from the combined check's lead, 2026-11-22 (its commit 9106577d7, docs/research/t0-gumm-catch-up-spread/README.md, "The three codes: the table, the rule and the recommendation"). Read by the line orchestrator. Every code is built on the line's merge 710834cc0, without main's step 2.

Card job 3, scale changes run 1 / run 2 (and the largest fall where it is above the noise of one change of about 0.004). Start is the largest fall only:
- Today's cliff: start 0.233 / 0.238; off 54 / 53; on 53 / 53; cut 56, 0.139 / 56, 0.144.
- A, the cap (what merges): start 0.235 / 0.238; off 0 / 0; on 0 / 1; cut 56, 0.141 / 56, 0.140.
- B32: start 0.086 / 0.087; off 0 / 0; on 4 / 1; cut 3 / 3, rises only.
- C32: start 0.085 / 0.086; off 0 / 0; on 1 / 0; cut 4, 0.002 / 3, rises only.
- B16 and C16 move the scale after off, after on and at the cut in every run.
- No catch-up ray after a reset (job 2, one run): start 0.070; off 0; on 0; cut 6, rises only.
About 0.070 of every start's fall is the dear first draws of t58-np60, not the catch-up rays.

The Mac at the constant scale 2/3 (slowest region's settle in draws, boil over draws 1 to 59 in %, grain over draws 0 to 15 in %):
- Off: today 142, 0.276, 2.14; A 139, 0.267, 1.61; B32 142, 0.291, 2.11; C32 139, 0.266, 1.61.
- Start: today 139, 0.109, 0.86; B32, C32 and "none" 139, 0.114, 0.89.
- Cut: today 134, 0.106, 0.88; B32, C32 and "none" 133, 0.112, 0.87.
- The spread between runs with the event moved by 1 and 2 draws: boil 0.008 after off, zero at the cut.

What the catch-up rays buy, measured: one ray per restarted pixel (the share 1/32 over 32 draws) buys nothing that the measures see. C32 measures as A after a light change, and as "none" at the start and the cut. Today's 8 rays per restarted pixel buy about 5 % less boil at the start and the cut (0.109 against 0.114, 0.106 against 0.112) and no faster settle. Their price on the card is a fall of the render scale of about 0.16 at the start and 0.14 at the cut.

The orchestrator's rule applied: (1) today, A at the start and the cut, B16 and C16 fail; B32 sits at the limit; C32, C' (the share on the reset's clock only) and "none" pass. (2) C32, C' and "none" measure the same picture. (3) "None" is the least code: the plan gives no own ray to a young history whose cap is 8 draws or more while fewer than 32 draws passed since the last reset. It reads only trace.drawIndex, which the plan has. C and B need a new read of the last draw's sample-age images with two barrier() calls, checked only on MoltenVK.

The lead's recommendation, which the line orchestrator shares: the cap merges as it is, and "no catch-up ray in the first 32 draws after a reset" is the fix of ter-4iic after the merge, with tests first. If the eye finds the first draws after a start or a cut too noisy in the videos, C' at 1/32 is the next step. The decision is the orchestrator's.

The final videos, checked by the line orchestrator with ffprobe and by frames: pics/cut-final.mp4 (141 frames), pics/start-final.mp4 (120 frames), pics/switch-final.mp4 (320 frames), each 3840 x 720 (three panes of 1280 x 720: A | today's cliff | C32), 60 frames a second, H.264, yuv420p, CRF 12, one draw per frame, each code on its own card scale log (run 2 of job 3), labels, the render scale per pane, a draw counter. They meet the rules. The cut's video is in 9106577d7; the start's and the switch's were not committed at 21:50.

Open at 21:55, asked of the lead: region r11 after the cut is 1.7 to 2.1 % further from its last frame in C32's pane than in A's pane at 2, 10 and 40 draws (videos.txt), where the table at 2/3 says the same picture. The lead says which causes it: the render scale, the catch-up rays or the reference frame.

Answer 85 of the line orchestrator: the videos keep the C32 pane and get no fourth pane for "none". The two codes measure the same at the start and the cut at 2/3, and their card logs are close (start fall 0.070 against 0.085).

**2026-11-23T02:54:40Z**

The orchestrator's answer of 2026-11-22, about 22:15, and the code finding of the line orchestrator.

The orchestrator's decisions: the cap merges as it is; answer 85 stands; the choice between "no catch-up ray after a reset", C' at 1/32 and a ray budget waits for two gaps. Gap 1: the start and the cut ran in the cabin with its lamps on, where direct light makes most of the picture, so the test cannot show what a bounce ray buys. The fair test: a start and a cut in bay-sunbeam and in the cabin with its lamps off, the codes today's cliff, "none" and C32, the same measures, and the grain per draw for draws 0 to 15. Gap 2: 4 times the rays for 8 draws give 3 to 5 % less grain, where the ray count alone predicts about half; answer from the code who reads an own ray's sample. If the bounce scenes also show no gain and the code says why, the orchestrator prefers to remove the cliff altogether (FullRateFrames as a ray rule), if the films of motion allow it. The fair test comes after the region test's first step and does not hold the merge.

Gap 1, the fact: the Mac's start and cut measures used the same scene as the videos, the cabin's person camera with the lamps on (tools/macrun.sh, start.json and cut.json). So the objection holds for the measures too.

Gap 2, the code (the line's head f223c7b85; the files are in src/Tertius.Rendering/Shaders):
- The plan (screen_trace_plan.comp, main): the block's chosen pixel traces the block's ray and never an own ray. Each other pixel of the 2 x 2 block gets an own ray where its history is shorter than FullRateFrames or the block's ray does not fit it. So a fully young block traces 4 rays in place of 1.
- The resolve (screen_trace_resolve.comp, recordAt): a pixel's sample comes from exactly one ray per draw, its own ray where the plan gave it one, or else its block's ray, reweighted for the pixel. So an own ray gives its pixel no extra sample. It makes the pixel's sample independent of its three block mates.
- The temporal blend (accumulate_temporal.comp): it reads the sample at the pixel only (imageLoad(sampleImage, pixel)) and blends it with the weight 1 / length. An own-ray sample and a block-ray sample take the same weight. A history of length n holds n samples in both codes, so the noise of one pixel's history is the same.
- The fix (accumulate_spatial.comp, step 3): histories shorter than HistoryFixFrames (4) take the length-weighted mean of 3 x 3 pixels spacing = 1 << (fixFrames - 1 - floor(length)) apart: 4 pixels at length 1, 2 at length 2, 1 at length 3. Taps 4 and 2 pixels apart read one pixel per block, so they read 9 independent samples in both codes. Only the taps 1 pixel apart, at length 3, read more independent samples with own rays.
- The bounce light's filter (DiffuseFilter.cs, diffuse_filter_wavelet.glsl): six dispatches of a 3 x 3 kernel with taps 1 << dispatch pixels apart, each reading the output of the one before, so the cascade pools every pixel within 63 pixels along each axis that the edge stops let through. Dispatch 0 writes its light back into the slow history. Here the own rays count: a pool of N pixels holds N independent samples per draw in place of about N / 4, so the pooled noise is at most halved.

What follows (the line orchestrator's reading, not measured):
- The samples are pooled, so "the filter never pools them" is not the answer. The answer is that the cliff buys independence inside a block, not samples per pixel.
- On an open surface the pool holds thousands of pixels, so the pooled noise is small in both codes, and what is left of it lies at the filter's scale (mottle and boil), not at the pixel's scale. The grain measure (the luminance minus its 5 x 5 box mean) sees only the pixel's scale, where both codes hold the same noise. That explains 3 to 5 %. The boil of 24 x 24 px blocks sees the filter's scale, but its mean over draws 1 to 59 dilutes the 8 draws of the cliff.
- So the cliff can show only in the boil and mottle of the first 8 draws, by a factor of at most 2, and on surfaces where the filter's pool is small: narrow surfaces, corners and jambs.
- For a ray budget (ter-8pco, ter-6fka): a ray reaches the picture only through the pool. A young history on an open surface is served by the filter. A budget buys most where the pool is small, or where the block's ray does not fit, and not simply where a history is young.

The fair test, sent to the combined check's lead at 22:30 as a continuation of this ticket: the orchestrator's scenes, codes and measures; my additions are the boil per draw for draws 1 to 15, one narrow-surface crop, and one control for the lead's early result 6. Early result 6 (its commit 6c03e34d2): under the card's scale, C32's grain over draws 0 to 15 is 1.08 against 0.87 for today's cliff at the start and 1.02 against 0.89 at the cut, with a fine speckle on the far floor at draw 2 (pics/start-draw2-floor.png); at the constant scale 2/3 the gap is 0.03. Today's cliff draws at a render scale of 0.76 to 0.85 there and C32 at 0.91 to 0.98, so the render scale and the rays are not separated. The control plays "none" back on today's card scale log. The lead's order: the region test's first step, one reviewer and the final JSON for t0-3vwx alone, then the fair test.

The lead's answer on region r11 after the cut: the gap lies in the reference frame. The center's display-linear mean is equal at draws 2, 10 and 40 (A 0.1642 / 0.1750 / 0.1853, C32 0.1641 / 0.1743 / 0.1854), and C32's last frames are 2.0 to 2.9 % brighter. The probe scene luminance of the two codes agrees within 0.1 %. The likely cause is FSR's history after A's 40 size changes; not proven.

Answer 86 of the line orchestrator: the code finding stays in this ticket for now. The orchestrator owns the record of ter-8pco and the design of ter-6fka and copies it there when the fair test confirms it.

**2026-11-23T03:18:50Z**

Correction by the line orchestrator, 2026-11-22 22:20 by the machine's clock: the clock times in my notes and messages of this evening after 21:50 were guessed and ran ahead. The messages that I labelled 22:00, 22:30, 22:40, 23:20 and 23:25 were sent between about 21:50 and 22:17. The commit times of the line's branch are right. From here on I read the clock before I write a time.

**2026-11-23T04:14:48Z**

Early result 7 of the combined check's lead, 2026-11-22 about 23:10 by the machine's clock: the fair test, on the Mac at the constant scale 2/3, in frozen films. Pictures in the lead's worktree, not committed at 23:15: docs/research/t0-gumm-catch-up-spread/pics/fair-bay-draw2.png, fair-bay-draw8.png, fair-chart.png, fair-cabin-draw2.png. The line orchestrator looked at the bay's two pictures and the chart: at draws 2 and 8 after the start, today's cliff gives a smooth bounce light, and "none" and C32 show blotches over the floor, the back wall and the right wall, with a different spread of the light.

Measured, start (the cut is bit-identical to the start, see below). Slowest region's settle in draws; boil and grain in %:
- bay-sunbeam, today: settle 164; boil 1-59 0.309; boil 1-8 1.370; grain 0-15 4.44; narrow crop boil 1-8 1.127, grain 0-15 0.77.
- bay-sunbeam, none: 164; 0.372; 1.801; 6.80; narrow 1.808, 1.10.
- bay-sunbeam, C32: 164; 0.369; 1.798; 6.66; narrow 1.817, 1.10.
- cabin with its lamps off, today: 133; 0.318; 1.026; 3.06; narrow 1.391, 2.02.
- cabin, none: 133; 0.322; 1.069; 3.12; narrow 1.457, 2.13.
- cabin, C32: 133; 0.320; 1.055; 3.13; narrow 1.411, 2.10.
- The spread over the start and the starts with the world run 1 and 2 ticks longer: bay settle 5 draws, boil 1-8 up to 0.019, grain up to 0.08, narrow boil 1-8 up to 0.051; cabin 0.011 or less.
- The bay's grain per span, today against none: draws 0 to 2 7.24 against 6.62; draws 3 to 8 3.34 against 7.31; draws 9 to 15 4.19 against 6.45. The bay's boil of draws 9 to 59: 0.142 against 0.148.
- The control for early result 6: "none" played back on today's card scale log at the lamp cabin's start gives a grain of 1.00 and a boil of 0.118 (today 0.87, C32 on its own log 1.08). So the rays cause most of that gap, and the render scale the smaller part.

What it says:
- In a bounce-lit scene the catch-up rays of today's cliff buy a clearly better picture in the first draws: 24 % less boil in draws 1 to 8, 38 % less on the narrow crop, and about half of the grain from draw 3 to draw 15. The settle is the same. The user's view holds there.
- One ray per restarted pixel (C32) buys nothing: it equals "none" within the spread.
- In the dark cabin the gain is small (4 % of the boil of draws 1 to 8).
- The line orchestrator's reading of the code (22:00 note) was right in size, about half, and that the narrow surfaces gain more. It was wrong about where: it said the grain measure would not see the gain, and the grain shows it from draw 3 on. The lead's reading, not measured: the correlated block samples of the first draws enter the history as blotches and stay until the history replaces them.

A fact about the engine, found by the lead: FrameHistory.Begin sets DrawIndex to 0 at every reset, and the noise and the FSR jitter follow the draw index. So every reset draws the same noise sequence, and in a frozen film a cut moved by 1 or 2 draws gives the same pictures to the bit. The spread between runs was taken with the world run 1 and 2 ticks longer.

Answers of the line orchestrator, which the orchestrator can reverse:
89. Before the card job, a sweep of shapes on the Mac in the bay's start: today (1, 8, 0), none, (0.5, 16, 0), (0.25, 32, 0), (0.125, 32, 0) and one front-loaded shape. Reason: C32 is out, and the open question is the user's own, whether the same number of catch-up rays spread over more draws keeps the picture. Then the card job and the video with the panes today | none | the best middle shape.
90. No scratch offset on the draw index for a noise-phase spread, and no video of the dark cabin. The bay's differences are far above the spread, and the cabin's small difference decides nothing.

**2026-11-23T04:16:34Z**

The orchestrator's decisions on the fair test, 2026-11-22 about 23:15: 'no catch-up ray after a reset' as the fix of ter-4iic is out, the removal of the cliff is out, and C' at 1/32 is out, by the bay's picture (docs/research/t0-gumm-catch-up-spread/pics/fair-bay-draw2.png in the combined check's worktree). Answers 89 and 90 stand. The cap still merges as it is, with one new gate of the merge (t0-iido): a take of a light change in the bounce-lit bay, the cap against today's cliff, draws 1 to 15, because the fair test did not test a light change in a bounce-lit scene. Added to the sweep: the shape (1, 3, 0), full rate for draws 0 to 2 only, and the ray total per restarted pixel beside each shape's measures. Added to the card job's table: for today's cliff, the number of draws that the render scale stays more than 0.02 under its rest after the event. A second design for the cost, named by the orchestrator and not built: the renderer asks for a lower render scale for exactly the draws of the catch-up rays and returns at once, a budget in pixels in place of a budget in rays over time; if the scale stays low for about 60 draws, most of the cliff's price is the controller's slow return (t1-zeyk, ter-5kz6). Both designs are named in the record when the sweep reports, and the orchestrator chooses. The engine fact of the draw index is in tools/README.md, 'The measures'. All sent to the lead at 23:16.

**2026-11-23T04:25:48Z**

The sixth gate's take of the merge (the cap after a light change in the bounce-lit bay) and the card column of the scale's return are recorded in t0-iido, 2026-11-22 23:25.
