---
id: t0-f8oc
status: open
deps: [t0-3kyb, t0-d3vd, t0-e8rh]
links: []
created: 2026-11-13T01:01:07Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Take the evidence census and the settle, lag, walk and stand runs of Design X on the Mac

Design: docs/plans/bounce-light-confidence.md (Design A with Design X's evidence), section 23. Branch t7-3e22-retained-deviation. Step 16, takes T12 to T14 (section 21). Long untimed renders on the Mac under the shared GPU lock (bash /Users/alexispurslane/Development/tertius/tools/gpu-lock.sh --shared <log> <command>), each hold 20 minutes or less; the short timed frame-span runs of T12 take the lock without --shared. The workbench only to fan out. T12 also gives open point 1 (a moved object cuts or only narrows the box) its numbers on rigs-crowd and docking-landing. Acceptance 5 to 9 and 12.


## Notes

**2026-11-14T18:49:14Z**

2026-11-14, takes T12 to T14 (files in /tmp/t7-3e22/takes). Candidate 287ba6e91 (before t0-6gej); control main d4670f80c plus scratch 797a3b2a9 (renames the probe's 'stops' local). Cloud $0.21, the rest on the Mac; each comparison on one machine. Acceptance 5, slowest region off/on draws: cabin X 83/not settled in 100 against main 71/54; bounce alone X 99/none, main 60/65; reflections alone X 61/none, main 118/97; so the cabin fails in the bounce light and acceptance 8 does not apply. A cut history settles dark: r11 16 % below the lamps-off control 10 draws after the lamps go off, 8 % below the lamps-on control 100 draws after they return; all regions end 2.7-3.9 % low against main's 1.3-2.6 %. bay-sun-off X 35/12, main 49/36; lamp-hall-16 X 35/25, main 32/25: pass. Acceptance 9: spot +0.09 to +0.10 pp, lamp-hall-16-moving +0.04 pp, bay-sun better: pass. Acceptance 6 (walk): X fails (r12 143, r11 81), main fails (r10 198). T14 stand: r11 settles in neither build within 355 draws after tick 306; last window X +10.0 %, main +5.2 %. T12 still census before t0-6gej, light evidence per block-draw (bounce / reflections): rect-lamps 1.94e-2 / 5.13e-3, exit-cabin 9.08e-3 / 1.72e-2, docking-bay 2.12e-3 / 4.48e-3; motion 0 everywhere. Open point 1: keep narrow only (cut doubles fast-cap blocks on rigs-crowd for no visible gain; on docking-landing blotch 0.162 against 0.135 but level +2.6 % against +5.3 % and flicker +11 %).

**2026-11-14T22:04:26Z**

2026-11-14, the cabin switch again after the merge of main (branch 73f4a6548 with t0-6gej and ter-1wo4, control main ab54c785e, Mac, files in /private/tmp/t7-3e22/x2/runs, tools in /tmp/t7-3e22/x2). Bounce light alone (TERTIUS_PROBE_SIGNAL=2). Draws to settle, slowest region off/on, then whole image off/on: branch 101/not settled within 100, 20/83; main 60/65, 39/48. So t0-6gej did not remove the dark settle. The branch's still controls hold 21 % more light than main's (whole image), 30 to 35 % more in the center region r11, and equal the clamps-off reference's controls to 3 decimals.

The cause, by variants of a scratch probe (TERTIUS_X_LIGHT_ACTION, never merged). Values are r11's light over the same build's control, minus 1, 15 draws after the lamps go off / 15 draws after they come on / 99 draws after they come on:
- branch as built (narrow box and cut to FastCap): -24.4 % / -26.1 % / -8.9 %
- cut only, wide box kept: -19.1 % / -22.2 % / -7.4 %
- narrow box only, no cut: -3.2 % / -16.1 % / -7.6 %
- no action of evidence: +8.5 % / -8.8 % / -4.1 %, the same as the clamps-off reference
- cut with no clamp at all: equal to cut only to 3 decimals, so the wide box clamps nothing after a cut
- cut only with block rays always (FullRateFrames 0): -16.5 % / -22.6 %, so the pixel's own rays are not the cause
- cut to 8 draws in place of 4: -4.4 % / -16.7 %
- main: +6.1 % / -11.9 % / -2.6 %
Census of the switch: light evidence covers 75 to 92 % of the blocks for 7 draws, from 3 draws after each switch, and under 0.2 % of them 16 draws after it.

Finding 1, the larger part: the camera jitter has a cycle of 18 draws (FSR, 8 x 1.5^2), and in r11 one draw of the 18 brings most of the sunlit sill strips' light (the fault of t0-qwx8). In the still control, r11 steps up by 2.4 % in that draw and falls by 0.3 to 0.5 % in each other draw (main: +8.3 %, then -0.8 %). Every series, also main's and the reference's, steps 2, 20 and 38 draws after the switch. A history cut to 4 draws holds none of that draw's light until the next one, so it reads about 20 % low, and the room's later bounces then rebuild slowly. With the jitter off (TERTIUS_DEBUG_JITTER=none) no build falls below its control in r11: cut only reads +21.4 % / -9.6 % / -3.2 % against main's +22.5 % / -17.3 % / -3.4 %.
Finding 2, the smaller part: the box that light evidence narrows cuts rare bright samples of the new light (ter-18hc's mechanism). The whole image falls 4.5 % below its control 15 draws after the lamps go off (cut only: +0.3 %), and it falls from -14.7 % to -18.5 % in the 4 draws after the lamps come on, the wrong direction (cut only: -13.7 %; also with the jitter off: -18.8 % against -13.5 %).
Finding 3, the pace: the clamps-off reference itself is 5.8 % low 60 draws after the lamps come on and 3.4 % low after 99 (time constant about 69 draws, so about half of the cabin's bounce light comes from second and later bounces, which carry no light age). Main ends 1.4 % low, because its narrow box follows the fast history.

Other variants (bounce alone, slowest off/on, whole off/on): narrow box on the first evidence draw only 83/none, 7/72; length bounded by the change's age plus 4 draws with one narrow draw 62/none, 8/88; restart from the pixel's own fast history 105/none, 66/50 (r11 flashes +46 %); RecentLightAge 16 with cut only 93/36, 64/18 (the evidence then holds the jitter's bright draw at a length of 4: +12 % flash 20 draws after the lamps go off); RecentLightAge 28 80/99, 46/18.
Full image, slowest off/on and whole off/on: branch 92/none, 10/54; cut only 66/none (r11 ends -6.0 %), 12/27; main 75/52, 36/24. bay-sun-off: branch 35/12, cut only 36/13. lamp-hall-16: 36/25 both. Lag error medians equal to 0.001 in the three lag films.
