---
id: lin-4ffc
status: open
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
