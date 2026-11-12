---
id: ter-18hc
status: in_progress
deps: []
links: [tiunr-wk29, tiunr-nxtr, ter-9371, td-hjn6, ter-wkvw]
created: 2026-11-04T20:48:47Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# The slow history's clamp cuts rare bright samples: a small glowing share reads low

Found in ter-gayt, part 5. A floor row in front of a glowing panel (glow 2, sky 0.2, 160 frames) reads these panel shares in the diffuse slow history, against the form factor:

| Distance to the panel | Form factor | Slow history | Mean of the two fast histories |
|---|---|---|---|
| 4.95 m | 0.0433 | 0.0236 | 0.0368 |
| 3.95 m | 0.0689 | 0.0437 | 0.0593 |
| 2.95 m | 0.1144 | 0.0879 | 0.1019 |
| 1.95 m | 0.1955 | 0.1741 | 0.1726 |
| 0.95 m | 0.3292 | 0.3439 | 0.3566 |

The first frame's rays are unbiased (the C# walk and the GPU's own rays hit the panel at its form factor). The bias comes from step 4 of accumulate_spatial.comp: the slow history is clamped into mean ± clampSigmas · sigma of the fast history over 3 × 3 pixels. A bright sample that arrives in a few percent of the frames is mostly outside that box. The screen tracer's block rays make it worse: the four pixels of a 2 × 2 block share one ray, so the 3 × 3 neighborhood holds about four independent samples, and its sigma is small.

What the user gets: a glow that covers a small share of a pixel's hemisphere, such as a lamp far across a room, lights the surfaces about a third to a half too dimly in a still scene.

To do: measure the bias against the clamp's sigmas and against a neighborhood that spans more blocks, and choose a fix (for example a wider box, or a box from the fast history's own variance over time). The test AGlowingPanelBesideAFloorPixelTakesItsShareOfThePixelsDiffuseLight measures at 0.95 m, where the bias is small. Move it to 4.95 m when the fix lands.


## Notes

**2026-11-09T12:08:39Z**

2026-11-09 user decision: keep the diffuse slow history's clamp. Measured in /private/tmp/tertius-diffuse-clamp-20261108/: it removes 22 to 27 % of the orbit cabin's indirect diffuse light (11 to 14 % of the image), 0 to 5 % in lamp-only scenes, and it hides the blobs that the diffuse filter makes of sunlit specks. Without it, the diffuse light takes 1.5 to 3 times as many draws to settle after a lamp switch. The user accepted the bias over ReSTIR GI (days of work, about 1 to 2 ms). If correct bounce from small bright sources matters later, ReSTIR GI is the recorded path.

**2026-11-12T06:58:36Z**

Decision for the user (2026-11-12, overnight). Branch ter-18hc-clamp-on-evidence is ready to merge but NOT merged: the orchestrator's rule, because the trade below is the user's to make.

What the branch does: (1) the diffuse slow history's clamp acts only on evidence of a change: screen motion of at least 1/32 pixel per frame, or a same-surface sample in the 3 x 3 pixels whose rays saw light younger than the history (the light age); (2) ter-wkvw: the light age also cuts the diffuse history to the change's age, as it cuts the specular one, when the sample holds at least 25 % of the history's light. The specular clamp is unchanged.

Films and stills, main against branch, matched probe builds of the same source (probe-m2 = main c4d13cb9d, probe-w2 = branch working tree), survey of tools/renderer-survey.py, 2026-11-12 00:35-01:18. Results: /private/tmp/t7-3e22/s/{m2,w2}/results.

| Film or still | Measure | Main | Branch |
|---|---|---|---|
| Stills rect-lamps, bay-sunbeam, docking-bay | bounce light against a reference with both clamps off | 77 to 92 % | 97.5 to 99.6 % (gate measurement, /private/tmp/t7-3e22/s/truth) |
| rect-lamps still | specks per 10,000 px | 7.95 | 6.81 |
| rect-lamps still | blotch score (clouds) | 0.058 | 0.115 |
| bay-sunbeam still | blotch score | 0.043 | 0.035 |
| docking-bay still | blotch score | 0.046 | 0.035 |
| cabin lamp switch, off | slowest region / whole image settles (draws) | 74 / 37 | 80 / 31 |
| cabin lamp switch, on | slowest region / whole image settles (draws) | 55 / 27 | not within 100 / not within 100 (whole image -7.0 % at draw 99, main -1.0 %) |
| bay, sun switch off | slowest region / whole | 47 / 27 | 52 / 24 |
| bay, sun switch back on | slowest region / whole | 35 / 20 | 13 / 7 |
| lamp hall 16, switch | off / on | 36 / 24 | 36 / 25 |
| bay sunbeam lag | median / largest lag (draws) | 26 / 64 | 17 / 67 |
| lamp hall 16 moving | median / largest lag | 4.5 / 62 | 4.7 / 63 |
| lamp hall 64 spots moving | median / largest lag | 1.2 / 11 | 0.3 / 18 |
| cabin walk, after the stop | slowest region / whole image settles | 179 / 108 | never within 200 / 153 |
| cabin walk, first draw after the stop | whole image above its final light | +32 % | +14 % |
| walk agent's stand film (ter-g3tk), step near tick 303 | middle region drains | about 2 x faster | about 2 x slower |

Why the branch drains slowly where it does: the clamp's evidence comes only from changes of direct light that the screen shows. A change only in bounce light (the walk agent's sill pixels near tick 303: 437 sunlit sill pixels change, but no ceiling, wall or floor pixel), a moving occluder between a light and a receiver (review finding B2), or a lamp whose light reaches a region only by bounce, gives no evidence. There the clamp stays off and only the blend of 1 / length (cap 32) drains the old light. In addition, after the gate has been open, the clamp's dark bias returns and drains away again at the same slow pace once the gate shuts: that is the cabin lamp-on case, where the branch is darker than its own steady light for more than 100 draws.

The two options:
A. Merge now. The user gets steady bounce light at 97.5 to 99.6 % of the truth instead of 77 to 92 %, and faster sun lag and sun-on settle. The cost: the cabin lamp-on and the cabin walk settle slower than main (never within 100 or 200 draws), and every bounce change without direct-light evidence drains about twice as slowly. The rect-lamps still shows twice the blotch score.
B. Wait for a fix of the no-evidence case. Design A (retained deviation per pixel, the pair agent's prototype /private/tmp/pair-mottle/design-a/retained-deviation.patch) is the candidate: a box from the history's own long-run deviation could include rare bright samples, so the clamp could stay on everywhere without the dark bias, and the deviation itself could carry evidence from the bounce rays. The user keeps main's behavior (dark by 8 to 23 %, fast drain) until then.
Recommendation: B, because the cabin lamp-on and walk regressions miss the ter-8hyd goal of settling within 60 draws by a wide margin, and Design A may remove the trade-off. If the user prefers A, the branch merges as it is.

Known uncovered case, for Design A: a bounce change without direct-light evidence where the bounce changes (ter-g3tk's tick-303 step; a moving occluder).

**2026-11-12T09:32:37Z**

Hardening, 2026-11-12. tools/mutate.py, 16 listed mutants (/private/tmp/t7-3e22/mutants.json, results /private/tmp/t7-3e22/mutate and mutate-g03). No Core, Simulation or Physics line changed, so no Stryker run.

| id | place | change | result | answer |
|---|---|---|---|---|
| G01 | accumulate_spatial.comp:29 | motion is no evidence | killed | AnImageThatMovesOnScreenIsEvidenceOfAChangeForTheClamp |
| G02 | accumulate_spatial.comp:36 | a neighbor on another surface counts | survived | Judgment: the same `sameSurface` helper guards the clamp box and the young-history repair, whose tests cover it; a test here needs a second surface in the accumulator harness, which has one wall. |
| G03 | accumulate_spatial.comp:39 | an age equal to the length counts | killed (after a new case) | ALightChangeAsOldAsTheHistoryOrOlderLeavesTheHistoryAsItIs(code: 17) |
| G04 | accumulate_spatial.comp:39 | any age counts | killed | ALightChangeAsOldAsTheHistoryOrOlder... |
| G05 | accumulate_spatial.comp:39 | only the pixel's own age counts | killed | AChangeThatANeighborsRaysSawOnTheSameSurfaceOpensTheClamp |
| G06 | accumulate_spatial.comp:42 | every history counts as changed | killed | InAStillViewRareBrightDiffuseSamplesKeepTheirShareOfTheSlowHistorysLight |
| G07 | accumulate_spatial.comp:107 | gate on the specular signal instead | killed | ScreenTraceRenderTests (panel and mirror) |
| T01 | accumulate_temporal.comp:316 | cut to the age plus 1 | killed | ALightChangeThatTheRaysSawRestartsTheHistoryAtTheChange |
| T02 | accumulate_temporal.comp:308 | no diffuse cut | killed | AfterALastingDimmingTheDiffuseHistoryKeepsOnlyTheNewLight |
| R01 | screen_trace_resolve.comp:90 | a ray without light still gives its age | survived | Judgment: such a sample has no light, so it never holds 25 % of a history's light and cannot cut; as a neighbor's evidence it opens the clamp only where its record changed anyway. |
| R02 | screen_trace_resolve.comp:91 | the resolve writes no diffuse age | killed | AfterALastingDimmingTheDiffuseHistoryKeepsOnlyTheNewLight |
| R03 | screen_trace_resolve.comp:70 | the early-out leaves the diffuse age unwritten | survived | Equivalent: the early-out pixels show no surface, the accumulation does not run for them, and as a neighbor they fail the same-surface test, so nothing reads their age. |
| C01 | HistoryInstrumentation.cs:42 | the temporal dispatch takes no light-age variant | killed | HistoryShaderTests, ShaderVariantTests |
| C02 | DescriptorLayouts.cs:111 | light pyramid on the diagnostic's binding | killed | LightAgeHistoryTests (validation) |
| C03 | ScreenTracePass.cs:209 | no barrier for the diffuse age | killed | ScreenTraceRenderTests (validation) |

Review (reviewer agent): no serious bug. Fixed: B1 (the 10-frame dimming test now steps to 0.02, so the clamp, not the cut, follows it), F1 (three wiring tests: the real resolve's diffuse cut, motion, a neighbor's age), F2 (ARCHITECTURE's storage-image count 19, 20 under the diagnostic), Q1 (diagnostic doc), Q2 (one macro: every precise build defines TERTIUS_LIGHT_AGE on its compile line), B3 (half-float note on LeastMotion). Open for the user: B2, a moving occluder gives no evidence (the same class as the no-evidence bounce change). Not done: Q3 to Q5 (naming and structure of the variant selection), judged out of scope. Suites: Checked and Release, all modules, 0 failures (2026-11-12 02:16, before the G03 case).
