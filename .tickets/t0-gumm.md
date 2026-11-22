---
id: t0-gumm
status: in_progress
deps: []
links: [ter-4iic, t0-o7gf, t0-zmjx, t58-np60, t0-qvm1]
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
