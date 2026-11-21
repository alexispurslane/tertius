---
id: lin-4ffc
status: closed
deps: []
links: []
created: 2026-11-15T16:01:38Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The retained deviation's rounding test weighs the step with σr's weight, not the fast history's: at a length of 32 it drops real steps up to 2.1 % of the light

Found in the review of t8-69v7, 2026-11-15. accumulate_temporal.comp on the Design X line zeroes a sample's step when abs(step) * weight <= BlendRounding * before, where weight = 1 / min(historyLength, DeviationFrames) is σr's weight and before = luminance(fast). The step goes from the fast history F, whose blend has the weight 1 / min(historyLength, fastCapOf(cap)). The store's rounding stalls F, so the bound that it needs is abs(step) / fastLength <= BlendRounding * before. The two weights are equal only up to the fast cap's length, 4. At a length of 32 the test zeroes real steps up to 32 / 1536, about 2.1 % of F (main's form zeroes up to 0.26 %). For Gaussian noise with a deviation of 1 % to 2 % of the light, that drops most steps, and σr comes out about 12 % to 50 % low (the reviewer's estimate, not measured). The form predates t8-69v7, which halved its slack. The fix: compare abs(step) / fastLength with BlendRounding * before, and add a test that a long history's retained deviation of noise at 1 % of the light follows its samples' deviation.


## Notes

**2026-11-21T16:23:59Z**

Design of the fix, and the line's open checks of the store fix (line orchestrator, 2026-11-21). One ticket lead does both, on the line's head, which holds the line version and the main version of the store fix (t8-69v7).

The model. The temporal dispatch of the accumulation owns the retained deviation σr and its update. The rounding test in that update asks one question: is this sample's step only the stall of the fast history F's own blend? F's blend has the weight 1 / fastLength, where fastLength = min(historyLength, fastCapOf(cap)). So the test weighs the step with F's length, not with σr's weight. No new type and no new function. The changed lines in accumulate_temporal.comp, inside if (hasSample):

    float fastLength = min(historyLength, fastCapOf(cap));
    float before = luminance(fast);
    float weight = 1.0 / min(historyLength, DeviationFrames);
    float step = luminance(sampled) - before;
    step = abs(step) <= BlendRounding * before * fastLength ? 0.0 : step;

fastLength moves up from the fast history's blend, which keeps using it. BlendRounding keeps its value, 1/1536. Its comment, section 6 of docs/plans/bounce-light-confidence.md and ARCHITECTURE.md lose the sentences on the wrong weight and name this ticket's fix.

What the reader of the image gets: at a history longer than 4 draws, σr no longer drops real steps below about 2.1 % of the light, so the clamp's box there is as wide as the design says. The box can only get wider. So the risk is more rare bright samples inside the box, which the two stills below check.

Tests first, red before the fix:
1. A bounce history of 32 draws whose samples carry noise with a deviation of 1 % of the light holds a retained deviation within a stated bound of the update in double precision. Today it comes out low.
2. The same at 2 % of the light, if one test cannot hold both levels.
The existing tests stay green without a changed bound: a sample without noise keeps a deviation of 0 (the stall of F is still covered), ASettledLightThatMovesByThreePercentCountsAsSpread, and ALongHistoryRetainsTheDeviationOfItsSamples. A changed expected value needs a reason in the note.

Acceptance:
- The new tests are green. The Checked suite and then the Release suite pass on the Mac at the final head, one after the other. They are also the line's open suites of t8-69v7 after its fix stage.
- One tools/mutate.py list with the rows L01 to L04 of t8-69v7 and the rows of this fix, with the table in a note. The old list file is gone with the scratch folder. Its rows are the changes M01 to M04 of the main version's table in t8-69v7: the fast path from 2^-15, every tie rounds up, every tie rounds down (all in half_store.glsl), and slowHistoryValue unrounded (specular_history.glsl). Rows of this fix: at least the test with σr's weight again, and the test without fastLength.
- A snapshot test whose image changes gets a real review: the reference, the actual image and the FLIP map, opened and described.
- Two stills before and after the fix, each opened and described, with the survey's speck and daub scores: bay-sunbeam and exit-cabin. Not rect-lamps and not the cabin switch: that room holds the sun's shadow leak until its fix lands. By the user's bar: no new blotches, daubs or bright specks.
- No timing take: the change is one multiply in a pass that already has the value.
- One reviewer for the diff from the line's head at the launch.

Out of scope: the remedy for the bay film's trail (t0-acxm), new takes of the room of the shadow leak, and main's RoundingShare, which has the right weight.

**2026-11-21T18:14:30Z**

Build of the fix (ticket lead, 2026-11-21). Branch t3/fix-retained-rounding-weight, fast-forwarded to the line's head 2f9461158, which is the review base. Commit a4c472728 holds the fix, its test, the docs, the probe patch and the mutant list. The commit of this note holds the stall test, the crops and the README.

The fix follows the design. In accumulate_temporal.comp inside if (hasSample), fastLength = min(historyLength, fastCapOf(cap)) moves up, and the rounding test is abs(step) <= BlendRounding * before * fastLength. BlendRounding keeps 1/1536. Its comment, section 6 of docs/plans/bounce-light-confidence.md and ARCHITECTURE.md now name the fast history's weight and drop the sentences on the wrong weight.

Red runs at 2f9461158, before the fix. The new test is AccumulatorRenderTests.ALongHistoryRetainsTheDeviationOfSamplesThatSpreadByAFewPercentOfTheirLight. It is a theory at spreads of 1 % and 2 % of a light of 0.5, over 320 draws. Its bound is 0.5 % of the update in double precision. It gave 0.2813 at 1 % and 0.8981 at 2 %. A CPU model of the test predicted 0.275 and 0.899. After the fix: 0.99906 and 0.99999 (model 0.9991 and 0.99994). The reviewer's estimate of 12 % to 50 % low was too mild at 1 %: σr came out 72 % low.

Changed tests, each with its reason:
- ASettledLightThatMovesByThreePercentCountsAsSpread is now ASettledLightThatMovesByFourTenthsOfAPercentCountsAsSpread, a move from 0.25 to 0.251 (the half float 0.250977). At the fast weight 1/4, a move of 3 % passes both 1/1536 and the old 1/768, so the test no longer locked in BlendRounding. A move of 0.39 % has a weighted step of 0.098 %, between 0.065 % and 0.13 %. Main's test of the same name has the same values. Main's RoundingShare 1/384 at the weight 1/4 is the same bound as 1/1536 at fastLength 4. It kills R01 and R04 below.
- New: ASampleWithoutNoiseLeavesNoDeviationWhenItsFastHistoryStallsShortOfIt. From 0.2, F's blend toward 0.250488 stops at 0.25, two half-float units short. So every later draw has a step of 0.2 % of the light. The test holds σr below 0.01 % of the light after 500 draws. A CPU model gives 5.0e-6 for the fix and 4.8e-4 for R02 and R03, and the GPU gave 4.84e-4 on both mutants. It came after the first mutant run, where R02 and R03 survived: no test made F stall short of a constant sample.
- DiffuseFilterRenderTests.AfterAStepOfASampleWithoutNoiseTheRetainedDeviationHalvesWithin45DrawsOfTheHistoryReachingIt: only its comment, which named the old weight (32 / 1536, about 8 draws). It is now 4 / 1536 of the light, about 15 draws. Its assertions and bounds did not change.
- No other expected value or bound changed. ALongHistoryRetainsTheDeviationOfItsSamples and the filter's deviation tests pass unchanged.

The probe patch. tools/renderer-survey/probe.patch did not apply at 2f9461158: its LeastMotion hunk targets sample_age.glsl, but the constant moved to motion.glsl. The hunk now patches motion.glsl, with the same specialization constant 13. The other hunks apply unchanged.

The stills: the survey's still part (bay-sunbeam and exit-cabin, probe build, 4.08 stops) at 2f9461158 and at a4c472728. docs/research/lin-4ffc-rounding-weight/README.md has the table and four side-by-side crops at full size.

| Case | Build | Specks per 10,000 pixels | Blotch | Blotch RMS | Level |
|---|---|---:|---:|---:|---:|
| bay-sunbeam | before | 0.0694 | 0.02985 | 0.01888 | +0.00048 |
| bay-sunbeam | after | 0.0694 | 0.02985 | 0.01888 | +0.00047 |
| exit-cabin | before | 0.2074 | 0.01530 | 0.00958 | −0.00051 |
| exit-cabin | after | 0.1977 | 0.01530 | 0.00958 | −0.00051 |

In bay-sunbeam, 668 pixels of the shown still differ, by at most 6 levels of 255, and only 2 by more than 2 levels. In exit-cabin, 11 pixels differ, by 1 level each. I opened both stills and the four crops: the bay's sun strip, the bay floor, the cabin floor with its sun bands, and the cabin's left wall beams. In each pair the two sides look the same. No new blotch, daub or bright speck. This is the expected change: the clamp's box widens only where σr was low, and the box rarely binds in these views.

The mutant list: docs/research/lin-4ffc-rounding-weight/mutants.json, run with nix develop --command python3 -I tools/mutate.py at a4c472728 (logs in the lead's scratchpad). Rows L01 to L04 are the store fix's rows, with their table in t8-69v7. The rows of this fix:

| id | place | change | result | killed by |
|---|---|---|---|---|
| R01 | accumulate_temporal.comp:358 | the test with σr's weight again | killed | ASettledLightThatMovesByFourTenthsOfAPercentCountsAsSpread, ALongHistoryRetainsTheDeviationOfSamplesThatSpreadByAFewPercentOfTheirLight (both cases) |
| R02 | accumulate_temporal.comp:358 | the test without fastLength | survived, then killed | ASampleWithoutNoiseLeavesNoDeviationWhenItsFastHistoryStallsShortOfIt (new; found 4.84e-4, bound 2.5e-5) |
| R03 | accumulate_temporal.comp:358 | no rounding test | survived, then killed | ASampleWithoutNoiseLeavesNoDeviationWhenItsFastHistoryStallsShortOfIt (new; found 4.84e-4) |
| R04 | accumulate_temporal.comp:32 | BlendRounding 1/768 | killed | ASettledLightThatMovesByFourTenthsOfAPercentCountsAsSpread, ALongHistoryRetainsTheDeviationOfSamplesThatSpreadByAFewPercentOfTheirLight (1 %) |
| R05 | accumulate_temporal.comp:354 | fastLength without the fast cap | killed | ALongHistoryRetainsTheDeviationOfItsSamples, ALightAgeOfSevenOrOlderGivesTheBlockNoEvidence |

The second run was tools/mutate.py --only R02,R03 at 790fdc1a9's tree, with the new test. No survivor is left, so no row needs a reason or a judgment call. No Stryker run: no C# line of Core, Simulation or Physics changed.

Suites at 790fdc1a9:
- Checked (tools/test-all.sh -c Checked, on the staged tree of each commit): every project green. Core 1,207, Simulation 2,007, Physics 1,492 passed (8 skipped), Rendering 3,299 passed (11 skipped) of 3,310, Host 2,072 passed (8 skipped), Unions 59, Style 276, Slop 597.
- Release (tools/test-all.sh -c Release): every project green. Core 1,175, Simulation 2,026, Physics 1,493 passed (8 skipped), Rendering 3,298 passed (11 skipped) of 3,309, Host 2,072 passed (8 skipped), Unions 59, Style 276, Slop 597. The load flake lin-5bsm did not show.
- No snapshot test failed in either suite, so no reference image changed beyond its tolerance and none needed a review.

The review (one reviewer, diff 2f9461158..790fdc1a9): correctness pass, code quality pass, ticket fulfillment changes required. Its CPU models gave the same red and green numbers to five digits. The fix stage:
- T1, the notes were missing: this note and the note in t8-69v7.
- B1: below 2^−14, the least normal half float, a half-float step is 2^−24 and not a share of F. So the new bound covers F's stall only down to F of about 4.6e-5, where the old one reached about 5.7e-6. A pixel without noise below that keeps a σr of about 2^−23, 14 stops under the exposure. The shader comment, the plan's section 6 and ARCHITECTURE.md now say that σr stays exactly 0 at a fast light of at least 2^−14. No code change: nothing shows.
- C1: the plan's "stalls within 1/w whole steps" is now 1/wf.
- C2: ARCHITECTURE.md writes the test's weight as 1 / min(length, FastCap), as the same paragraph writes the fast blend.
- C3: the stall test's comment names R02's form, abs(step) <= BlendRounding * before, in place of "without the fast weight".
- C4: the filter's halving test now names the stall test as the test that holds a stall's deviation.
The fix stage changed comments and docs only. The suites ran again at the final head: see the next note.

**2026-11-21T19:00:59Z**

Suites at the final code head db4f14bb0 (ticket lead, 2026-11-21), after the fix stage. Checked, on the staged tree of that commit: every project green. Core 1,207, Simulation 2,007, Physics 1,492 passed (8 skipped), Rendering 3,299 passed (11 skipped) of 3,310, Host 2,072 passed (8 skipped), Unions 59, Style 276, Slop 597. Release, at db4f14bb0: every project green. Core 1,175, Simulation 2,026, Physics 1,493 passed (8 skipped), Rendering 3,298 passed (11 skipped) of 3,309, Host 2,072 passed (8 skipped), Unions 59, Style 276, Slop 597. No snapshot test failed, and the load flake lin-5bsm did not show. The commit of this note changes only this ticket.

**2026-11-21T19:02:22Z**

Integration (line orchestrator, 2026-11-21). Gates held: status ready_for_integration, no fork, the worktree clean at f69f31095 on branch t3/fix-retained-rounding-weight, one review (correctness pass, design pass, fulfillment changes-required for missing ticket notes), each finding resolved with evidence, both suites green at the code head db4f14bb0, nine mutant rows killed. Merged into t7-3e22-retained-deviation as f61187fde. The merged tree differs from the tested tree, outside the tickets, only by the line breaks of main's cleanup commit in GpuCalibrationBench.cs and StoreRoundingProbe.cs.

Last check, one read of the diff from 2f9461158. The shader change is the design's: one variable fastLength serves the rounding test and the fast blend, so the two cannot drift apart. The new tests state behavior: a long history retains the deviation of samples that spread by 1 % and 2 % of their light (red 0.28 and 0.90 of the exact value, green 0.999 and 1.000), and a stalled fast history leaves no deviation. I opened the wall crops of bay-sunbeam and exit-cabin: the two sides of each pair look the same, with no new blotch, daub or bright speck.

A fact for the line's records: before this fix the retained deviation came out 72 % low at a noise of 1 % of the light. Every earlier take of Design X, and the scratch probe on 73f4a6548 that t0-ion0 and t0-xdck use, has that narrower clamp box at long histories. The two stills changed in at most 668 pixels by at most 6 of 255 levels, so the earlier readings stand, but the room's takes after the shadow fix (t0-9fo0) run on a head with this fix.
