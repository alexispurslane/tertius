---
id: tiunr-okzm
status: closed
deps: []
links: [tiunr-7haj, tiunr-3am9, tiunr-lzc7]
created: 2026-11-08T05:28:59Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# The light-change age finds changes in the still reflections scene

Found 2026-11-08 in tiunr-7haj's screenshots. scripts reflections-long (copy at /private/tmp/tertius-light-age-default-20261108/shots/reflections-long.json, scene src/Tertius.Demo/DemoScenes.Reflections.cs) renders differently with the light age off and on, although no light changes: front view 43,888 of 921,600 pixels differ, at most 55 levels, 200 pixels by 16 levels or more, scattered specks on the floor right of the bar (diff /private/tmp/claude-diff/front-diff.png). The light age promised no evidence in a scene whose light holds still. Find which pixels and frames get evidence and why, before the box fix tiunr-nxtr relies on the light age.


## Notes

**2026-11-08T05:53:42Z**

Diagnosis (research sub-agent, 2026-11-08). Full report: /private/tmp/tertius-light-age-evidence-20261108/report.md. No engine source changed. A probe outside the repository (probe/, assembly name Tertius.Host.Tests) draws each still draw by draw through the renderer's DrawFrame and reads the light-age record, the sample age and the light-age-diagnostic cut lengths after each draw. With the diagnostic, its front/low/edge PNGs are byte-identical to a plain demo run and to shots/before-light-age.

Per still (33 draws, 848x480): front 1,667 resets and 973 cuts, 23-146 resets per draw with no decay. Low 4,140 resets (1,324 at draw 2 and 1,318 at the shown draw 32) and 11,890 cuts. At draw 32, 700 receivers drop from length 23.0 to 0.5. Edge 605 resets and 357 cuts. Draw 0 always has 0.

Causes, by counterfactual runs:
- The exposure moves the lamp's reach: sqrt(peak/E_cut), E_cut = pi R_cut / 2^highestStops. In the low still the reach goes 2.75, 3.46, 4.30 ... 4.13, then 3.31 m at the shown draw, because the shown draw's exposure step is 0. The ball's lamp side (about 3.5 m away) flips between 0 and about 0.0008 W/(m2 sr), an infinite relative change. This is 64% of the low resets and about 83% of the low cuts. Native rendering without jitter keeps exactly these, and 0 resets in front.
- H3 jitter at silhouettes and thin steps: front cuts fall from 973 to 231 without jitter. Native front: 0 resets without jitter, 2,831 with it.
- H4 the temporal shadow filter: under FSR, cascades and atlas tiles take 4 of 16 PCF taps per draw by (DrawIndex%4 + x + 2y)&3. With the exposure fixed and the jitter off, 32 front pixels still cycle with a period of 4. This is about 81% of the edge cuts.
- H1 start-up refuted (draw 0 has 0, no decay). H2 inexact undo refuted: the exposure moves by up to 0.034 stops per draw with 0 resets in native front without jitter. The plan runs before the adapt step. H5 refuted: 0 resets give 0 codes and 0 cuts.

The evidence lasts. In a window-like run (one draw per tick), the last 60 ticks average 42/16/10 resets and 51/22/4.6 cuts per frame (front/low/edge). The first 60 Hz frame after a still mass-resets (916 in front, 1,377 in low) as the reach drops from 728.8 m to about 2.3 m.

Image: only 1.2% of the front off/on difference pixels are cut receivers (11.9% within 6 px). The old light-age variants differ from the control only in the record, the code and the cut, so the cuts cause it all, mostly through the exposure. The off/on exposures differ by 1e-6 to 3e-5 stops at the shown draw. Fixed exposures 1e-3 stops apart reproduce the same cloud right of the bar, with the same largest difference of 55 levels.

Fix directions, not built: (A) an absolute floor of R_cut, undone by the exposure, in the change test, which kills the reach flips. (B) A 4-draw least/most envelope in the record, which kills H4 and most of H3 for 8 bytes per pixel. (C) Stop the exact light moving: a reach independent of the draw's exposure step, and 16-tap shadows for the record. New ticket tiunr-3am9 holds the renderer's own still reach mismatch.

**2026-11-08T12:33:28Z**

2026-11-08: cause found (/private/tmp/tertius-light-age-evidence-20261108/report.md): the reach follows the exposure, FSR jitter at edges, and the temporal shadow filter's 4-draw cycle, under a purely relative 2% test. The user chose the fix: a floor (a move counts only above CutoffRadiance with the exposure undone) and persistence (a move counts only after it stays outside the range for 4 draws in a row; the age then reaches back to the change's first draw; no new memory). The envelope design was rejected for its 7 to 15 MiB at 1280 x 720.

**2026-11-08T12:37:05Z**

2026-11-08: correction to the chosen design. A cut shortens only the history's length; it cannot remove old light that the 3 pending draws already blended in (after 64 old frames about 95% of the value is still old). So a committed change gets age 0, which drops the receiver's history, not age 3, which would leave about 72% old light on the commit draw, fading as 3/(3+k). Cost: the 3 pending post-change frames are lost.

**2026-11-08T13:40:42Z**

2026-11-08, implementation sub-agent: floor and persistence built (uncommitted). Output in /private/tmp/tertius-light-age-persistence-20261108/.

Design as built:
- Floor: a light moves only if it leaves the 3x3 range by more than 2% of the range's end AND by more than CutoffRadiance / exposureScale(), on the demodulated light (conservative form: base color <= 1, so no visible move escapes; the record cannot split glow from direct light). New GLSL constant CutoffRadiance in exposure.glsl (C# copy LightUnits.CutoffRadiance).
- Persistence: LightChangeDraws = 4 in screen_trace.glsl. Pending: the record keeps its last light and counts draws; back inside drops the count.
- Commit age 0, not 3 (user's choice in this session): the pending draws already blended the new light into receivers' histories, and a length cut cannot remove old light. Locked by AfterALastingDimmingTheReceiversBlendTakesNoOldLight (diagnostic length before blend = 0) and mutant M11.
- Found during the build: a border pixel whose range reaches the floor took the dimmed light silently, and the new light spread one ring per draw into the interior (209 of 971 interior emitter pixels never changed). Fix: inside the range, the record takes the new light only within the 2%/floor band of its own last light; otherwise it keeps its last light. Slow drift (<2% per draw) is still followed as before (ALightThatDriftsByLessThanTheShareInEachDrawCountsAsNoChange).
- Encoding: g = age +/- n/4 away from 0, n = pending draws 1..3; age = trunc(g). 8 bytes per pixel unchanged. Hits store g raw: ageCodeOf and rayFieldOf truncate toward 0.

Probe (same probe, before = exact reconstruction in a repo copy; reproduces the diagnosis numbers):
| still (33 draws) | resets before -> after | cuts before -> after |
| front | 1,667 -> 3 | 973 -> 12 |
| low | 4,140 -> 11 | 11,890 -> 32 |
| edge | 605 -> 142 | 357 -> 0 |
Window-like run, last 60 one-draw frames per view (resets/frame, cuts/frame): front 48.97/20.29 -> 0.15/0.14, low 21.42/46.90 -> 0.46/1.34, edge 9.90/4.58 -> 4.80/0.00. Mass reset after a still: 916 -> 3 (front), 1,377 -> 2 (low).
Edge residue: one column x=216, y=390-436 (47 px), light ~121 vs neighbors 155-161; FSR's 18-phase jitter at scale 2/3 keeps the sample on it 4 draws in a row, so it commits every 18 draws. No cut follows.

FSR plate captures (capture tool copies that also count cuts): rough 20% dimming persistent 90% adjustment 36 -> 40 updates before FSR, 92 -> 100 final output; unfinished after 1 update 0.436 -> 0.979 (plate). Still rough and smooth: all rendered files byte-identical to the tiunr-7haj captures; draws.jsonl equal apart from the added Cuts field. Walk (--camera walk, static rough and smooth, diagnostic on): 0 resets and 0 cuts per frame before and after; 252 render files byte-identical.

Mutation (tools/mutate.py in a repo copy, class LightAgeHistoryTests):
| id | place | change | result | answer |
|---|---|---|---|---|
| M01 | screen_trace_plan.comp:37 | no floor above the range | killed | |
| M02 | :37 | no floor below the range | killed | |
| M03 | :37 | min instead of max | killed | |
| M04 | :37 | > to >= above | survived | Judgment call: differs only when the light equals most + max(most*0.02, cutoff) exactly, which no test scene can hit on purpose. |
| M05 | :82 | floor without exposure undone | killed | |
| M06 | :82 | floor times exposure | killed | |
| M07 | :84 | inside range always takes light | killed | |
| M08 | :84 | inside range never takes light | killed | |
| M09 | :85 | pending +2 | killed | |
| M10 | :88 | >= to > at commit | killed | |
| M11 | :89 | commit age 3 | killed | |
| M12 | :89 | commit keeps old light | killed | |
| M13 | :90 | pending takes new light | killed | |
| M14 | :90 | pending count not stored | killed | |
| M15 | :77 | age keeps pending fraction | killed | |
| M16 | :63 | no sky check at center | survived | Judgment call: unreachable, a tap that passes the surface test shows a surface whose record light is >= 0; kept as a commented defense. |
| S01 | screen_trace.glsl:78 | fraction toward 0 for negative age | killed | |
| S02 | :84 | trunc to floor | killed | |
| S03 | :90 | decode scale 2 | killed | |
| S04 | :453 | hit reads raw g | survived | Equivalent: ageCodeOf and rayFieldOf truncate toward 0 like ageOfRecord. The decode was removed, so the hit line is the original. |
| S05 | :64 | LightChangeDraws 2 | killed | |
| E01 | exposure.glsl:15 | cutoff halved | killed | |
| E02 | :15 | cutoff doubled | killed | |
23 rows: killed 20, timeout 0, survived 3, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0
No changed C# line in src, so no C# coverage applies.

**2026-11-08T13:58:45Z**

2026-11-08: verified the report: suite2-Checked 10,336 run 0 failed, suite2-Release 10,323 run 0 failed; screenshots looked at, unchanged in look. Own-band rule reviewed in screen_trace_plan.comp; its predicted border staleness is ticket tiunr-lzc7.
