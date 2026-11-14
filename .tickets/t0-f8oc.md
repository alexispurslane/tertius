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
