---
id: t0-zmjx
status: in_progress
deps: []
links: [ter-4iic, t0-70v1, t0-o7gf, t0-gumm]
created: 2026-11-22T20:26:05Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Light evidence cuts a history to the shared ray's length, 8 draws, so a light change leaves no block of own rays

After a light change Design X costs about 5 ms more GPU time per draw than main for a block of 10 to 13 draws (t0-5tyj, 2026-11-22: peak 22.8 to 22.9 ms against main's 17.5 to 17.7 ms and a settled 15.3 ms, the fair cabin switch at the fixed scale 2/3). The cause: light evidence cuts a history to FastCap, 4 draws, for the 7 draws in which the light age is recent, and the ray plan gives a pixel its own ray while its history is shorter than FullRateFrames, 8 draws. So every pixel that sees the changed light traces its own rays for about 11 draws.

The user settled the fork on 2026-11-22, after the three-pane video, the cost chart and the table:
1. The restart after a light change: "8 draws". Light evidence restarts a history at FullRateFrames, as region evidence already caps it. The user accepts a fade that is about 5 draws slower after off and 3 to 4 after on.
2. The own-ray limit: keep 8 now, test 4 later. Nothing changes in the ray plan for the merge. The ticket t0-o7gf tests 4 draws for every young history after the merge.

This changes decision X1 of docs/plans/bounce-light-confidence.md (line 41: "a cut of L to FastCap").

## Design

The model. Evidence of a change of unknown size restarts (or caps) a history at the shortest length that keeps the block's shared ray. That length is one constant, FullRateFrames in Shaders/sample_age.glsl, 8 today. It has two readers: the ray plan (screen_trace_plan.comp gives a pixel its own ray while its history is shorter) and the spatial dispatch of the accumulation (accumulate_spatial.comp cuts a history to it). The cap is not a special case for light. It removes one: light evidence no longer has its own, shorter restart that the ray plan answers with own rays.

Who owns what. The length L of a pixel's slow history is one stock, stored in the slow history's alpha and owned by the accumulation. The spatial dispatch is the one place that cuts it on evidence. The ray plan only reads the last draw's L. Nothing new is stored.

The change, in Shaders/accumulate_spatial.comp, inside "if (historyLength > fastCap)". Today:

    float antiLagged = max(fastCap, historyLength * (1.0 - min(1.0, lag / parameters.antiLagShare)));
    if (evidence == LightEvidence)
        historyLength = fastCap;
    else if (evidence == RegionEvidence)
        historyLength = min(antiLagged, FullRateFrames);
    else
        historyLength = antiLagged;

After it, light evidence takes the branch of region evidence: historyLength = min(antiLagged, FullRateFrames). This is the line that the experiment XB measured (scratch-light-evidence-cap.patch in the acceptance record). It holds for every signal that takes light evidence, the bounce light and the reflections, as XB did. It only ever shortens a history. The lead chooses the form (one condition for both kinds, or a named predicate) and records it.

FastCap keeps three uses, none of them an evidence restart: the fast history's blend length (accumulate_temporal.comp), the gate past which the clamp and the anti-lag act, and the anti-lag's floor. Say so in the design doc.

Tests first, red on the present code:
- A lasting dimming leaves no history of a block with light evidence shorter than FullRateFrames because of the evidence, for the bounce light and for the reflections. The two present tests that expect the fast cap (LightAgeHistoryTests: "ByDefaultALastingDimmingOfTheEmitterCutsTheReflectionHistoryToTheFastCap", "TheCappedPreciseHistoryAlsoFallsToTheFastCap") change to the new length, with names that say it.
- The cause of the cost: after a lasting light change in a settled still scene, the ray plan gives no more pixels a ray of their own than before the change (count the plan's own-ray bits through a read-back of the plan image, in the draws while the light age is recent).
- The one rule: light evidence and region evidence cut a settled history to the same length.

Docs with the change: docs/plans/bounce-light-confidence.md (decision X1 with a record of 2026-11-22 and the user's words; the table row of LightEvidence in section 8; the reason "bounds the length of S by the fast window's length ... weight 1/5", which becomes the weight of a history of 8 draws; the code quote near line 539; FastCap's remaining uses), ARCHITECTURE.md (the paragraph that says "Light evidence cuts the length of every history of the block to FastCap", near line 1692, and its reason), the comments in accumulate_spatial.comp (head and step 4) and the comment of RecentLightAge in sample_age.glsl, whose arithmetic names the fast history.

Hardening on the changed lines: a list of mutants for tools/mutate.py (GLSL of Rendering; for example the cap's constant, min against max, the evidence condition) with one result each, and coverage of the changed C# lines if any. The Checked and Release suites before the merge into the line.

Base: the line's head with main ac5f23915 merged.

Out of scope: any change of the ray plan or of FullRateFrames (the after-merge ticket), of RecentLightAge, of the clamp, of the region's drift test, of the motion evidence.

## Acceptance Criteria

- The new tests are red on the present code for the right reason and green with the change. No other test changes its meaning.
- The fair cabin lamp switch at the fixed scale 2/3 on the Mac, measured as in t0-5tyj: no draw lies more than 1 ms above the settled GPU span after off or after on (XB: 0 / 0; Design X: 13 / 10 to 11), the settle is within 2 draws of XB's (slowest region 82 / 55, whole image 72 / 34), and the boil without the fade in the 60 draws after off is not above Design X's (0.26 / 0.29 / 0.32 %).
- The settled GPU span does not rise against Design X (15.34 ms in that take).
- The docs and comments say 8 draws and give the one rule for both kinds of evidence. Decision X1 holds the record of 2026-11-22.
- Every mutant of the list has a result: killed, equivalent with a reason, or a judgment call with a reason.
- Checked and Release suites pass on the merged code. One reviewer passes correctness, ticket fulfillment and code quality with evidence.


## Notes

**2026-11-22T20:27:22Z**

Lead: the acceptance lead, in its thread (thread:delegated-task:command%3Amcp%3A8450ae50-a640-49ce-ab14-e0397e6a826d%3Adelegate-task%3Adesign-x-line-t0-xlcu-lead-1) and its worktree t3-c3911144, branch t3/measure-regional-lag-tolerance. Brief sent on 2026-11-22 about 15:30. It may start one builder stage agent and must start one reviewer stage agent (claudeAgent, claude-opus-5-5, effort high). Base: the line's head with main ac5f23915 merged; I send the merge commit's hash. After the cap it fixes tc-8142 in the same worktree. The user's two answers of 2026-11-22: the restart after a light change is '8 draws'; the own-ray limit stays 8 now and is tested at 4 later (t0-o7gf). My answer (53): the acceptance lead takes the cap, because it holds the scratch variant and the measuring tools, and no new worktree with a build fits the disk (21 GiB free, 30 needed). My answer (54): tc-8142 goes to the same lead after the cap. My answer (55): the lamp switch film for the merge decision is taken again with the cap by the lead of t0-3vwx on a new scale log from the card, because the cap removes the cost block that pulled Design X's scale to 0.83 after each switch; the walk is not taken again.

**2026-11-22T20:30:27Z**

The user reopened the settlement on 2026-11-22, a few minutes after it. The user's words: "wait wait, I changed my mind. the main issue is just how many rays get drawn, right? why not reset to 4, but then hold off on casting a ray for the surface, despite being below 8, until the evidence has also stopped resetting it to 4, so the raycasting starts when the frames can actually accumulate again anyway, and build up from 4 to 8? let's at least also build that, and compare". So this ticket's production build is held: no production code, no change of the two tests that expect the fast cap, no change of decision X1, until the user chose again. t0-o7gf is not affected.

The third scratch variant, XD, "own rays wait for the evidence": the accumulation stays as Design X has it (light evidence cuts to fastCap, 4 draws); the ray plan gives a pixel no ray of its own while light evidence still holds its history at 4 draws; when the evidence ends, the present rule applies and the pixel traces its own ray while its history grows from 4 to 8. As a rule: a pixel traces its own ray while its history is young and can grow. It reaches only histories under light evidence.

My design notes for it, sent to the acceptance lead: the plan runs before this draw's rays, so the pending evidence is a derived read of the last finished draw: the block's youngest light code, aged by one draw, is still recent. Exact form: the plan reads the last draw's sample age of its 8 x 8 block; in a real build the predicate lives in sample_age.glsl beside recentLightCode. Proxy if the exact form passes about 40 scratch lines: hold back the own ray where the stored length is the fast cap itself; its faults are that own rays start one draw late (3 dear draws where a real build has 4), that a history pinned at the anti-lag's floor loses its own ray too, and that a growing history has none in the draw where its length is 4. The lead says which it built and whether the reach is exact.

To deliver: the table with the row XD (settle, GPU span settled, peak, draws above settled, boil), the cost chart with XD's line, one video X | XB | XD, the card's scale log through the switch for XD and XB, and the two-design table again, the cap against the new variant.

**2026-11-22T20:41:09Z**

Answer 58 of the line orchestrator, 2026-11-22: the orchestrator's 16 GiB floor ended at 15:50 (33 GiB free; the limits 22 / 20 / 30 hold again) and left the choice of the video's panes to me. The panes of Design X and of the cap in the video with the third variant are rendered again from the builds that exist, not decoded from the committed three-pane video, because a second encode softens the grain that the user judges. Fallback under 22 GiB: decode, and mark those panes as second-generation.

**2026-11-22T20:53:26Z**

Change of who decides, from the orchestrator, 2026-11-22. The user's words: "can you just use your best judgement and try to hill climb toward a complete/good lighting system from here? im utterly exhausted of choosing these in the weeds designs, and honestly with the wackamole we're playing, im worried theres some fundamental approach we're doing wrong". Forks now go to the orchestrator, who decides; still for the user: deletions outside the line's own evidence, money above the limit, anything outward-facing.

The orchestrator's rule for the choice between the restart at 8 draws and "own rays wait for the evidence", which the line orchestrator applies and the orchestrator confirms: (1) a variant must leave no cost peak that moves the render scale on the card's log; (2) among the variants that pass, the faster settle wins, unless its boil in the first 60 draws is higher by more than the difference between two runs of one code; (3) on a tie, the smaller reach and less code wins. After the table with the rule's application, the production build starts on 710834cc0 without a wait.

Answer 59 of the line orchestrator: a played-back run is deterministic on one machine, so "the difference between two runs of one code" is measured as the spread of the boil numbers over runs of the cap's build with the switch one and two draws later, after one repeated run confirms that equal runs give equal numbers.

The orchestrator also asks for root causes before one more rule: each report names the root cause that a rule acts on and whether a structural change removes the fault with others (here: the start-of-run cost, ter-4iic and t58-np60). A separate read-only review of the whole indirect lighting's approach runs at the orchestrator; it may reorder t0-o7gf and t0-70v1.

**2026-11-22T21:05:14Z**

The rule applied, 2026-11-22 (line orchestrator, from the acceptance lead's table). The hold ends; the production build of the cap is released on the line's head with the merge 710834cc0.

Point 1, the card's scale log (RTX 2000 Ada, target 16.67 ms, the glow-stays lamp switch, one job, $0.0449), changes and largest fall of the render scale from 10 draws before to 60 draws after a switch, off / on; idle = draws 133 to 192:
- The cap (XB): idle 0 changes; 0 changes, fall 0.000 / 0 changes, fall 0.000 (1.000 throughout). Passes.
- Own rays wait for the evidence (XD): idle 0 changes; 32 changes, fall 0.097 (0.998 to 0.901) / 32 changes, fall 0.099. Fails. Its GPU span per draw after off: draws 0 to 9 cost 17.7 to 18.7 ms (settled 17.6), draws 10 to 13 cost 25.2, 24.1, 25.3, 22.8 ms, draw 14 costs 19.8 ms. So the dear block moves behind the evidence and shrinks from 10 draws to 4, at the same height.
- Design X without a fix, same job: 43 changes, fall 0.133 / 41, 0.138. Main c4c1f1f2f, old job: 15, 0.041 / 18, 0.042. Main ac5f23915 has no such log.

Point 2 (the fair switch at the fixed 2/3 on the Mac; slowest region off / on, whole image off / on, boil 0 to 60 draws after off on floor / wall / ceiling): Design X 77 / 51, 67 / 31, 0.26 / 0.29 / 0.32; the cap 82 / 55, 72 / 34, 0.26 / 0.27 / 0.29; XD 77 / 51, 67 / 31, 0.26 / 0.29 / 0.33; main c4c1f1f2f 143 / 92, 123 / 55, 0.24 / 0.23 / 0.29. With one candidate left, points 2 and 3 decide nothing.

Point 3: the cap is 1 changed line in accumulate_spatial.comp and changes no ray plan; XD is about 20 lines in screen_trace.glsl and screen_trace_plan.comp, reads the last draw's sample ages in the plan (writeonly dropped, a read-after-write barrier from the last draw's resolve, not checked on Linux), and keeps the plan's reach exactly.

Root causes (the lead's reading, which I share): the plan ties the own-ray rate to the history's length, so a restart buys rays whether or not they can be used. The cap puts the restart at the plan's threshold, one rule for both kinds of evidence, and the fade pays about 5 draws after off and 4 after on. XD acts on the plan and still pays the 4 draws of growth from 4 to 8. Neither bounds the start-of-run cost; the structural candidate for that is a ray budget per draw (noted in ter-4iic).

My answers: 60, the run-to-run spread is not taken (one candidate); 61, the three-pane video and the chart are finished from the frames that exist, the Mac's cost runs of XD are dropped, the scratch patch and the card's logs are committed as evidence; 62, a new acceptance condition: on the card's scale log through the lamp switch, the production build shows no more scale changes in the windows from 10 draws before to 60 draws after each switch than in the 60 idle draws before the first switch.

**2026-11-22T21:05:54Z**

The orchestrator confirmed on 2026-11-22: the restart at 8 draws is the choice, by point 1 of the rule; answers 59 to 63 stand. The user's own variant got a fair test: 'own rays wait for the evidence' was built as a scratch variant with the plan's exact reach (scratch-plan-waits-for-evidence.patch in the acceptance record) and measured on the card and on the Mac. Its result in one line: the dear draws shrink from 10 to 4, each still costs up to 25 ms on the card (settled 17.6 ms), and the render scale still falls by 0.097 after a switch, where the restart at 8 draws leaves it at 1.000. The ray budget per draw is a candidate in the orchestrator's review of the approach and is not designed on this line now.

**2026-11-22T21:20:55Z**

The acceptance lead's evidence for the choice, 2026-11-22, commit ee71751c7 on t3/measure-regional-lag-tolerance: the video pics/switch-cabin-fair-fixed-23-x-xb-xd.mp4 (Design X | the cap | own rays wait for the evidence; H.264, 3840 x 720, yuv420p, 60 fps, 320 frames by my ffprobe; all panes from first-generation frames) and the card's cost chart pics/cost-switch-card-16ms-x-xb-xd.png in docs/research/design-x-acceptance-2026-11-22. The chart: Design X peaks at about 24.5 ms after each switch and dips to 14 to 15 ms while the scale recovers; the user's variant peaks about 6 draws later at about 25.5 ms; the cap stays flat at about 17.6 ms. Limit: the encode adds boil to each pane about equally (Design X's floor 0.57 % in the video against 0.25 % in the source frames). Reported to the orchestrator. The lead merged the line's head 6692e0bfe into its branch and wrote the cap's tests; the lead's bug ticket for the cost block is tc-g2zz.
