---
id: t0-o7gf
status: open
deps: [t0-iido]
links: [ter-4iic, t0-70v1, t0-zmjx, t0-gumm, t58-np60]
created: 2026-11-22T20:26:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# Can the shortest history length that keeps a block's shared ray be 4 draws and not 8? Test it for every young history

One question about one constant: can the shortest history length that keeps the block's shared ray be 4 draws and not 8? The constant is FullRateFrames in Shaders/sample_age.glsl. The ray plan gives a pixel its own ray while its history is shorter than it, and evidence of a change (light evidence since t0-zmjx, region evidence before) cuts a history to it.

If 4 holds, three things follow from the one change: both kinds of evidence restart at 4 draws, so the fast fade after a light change returns (settle 78 / 51 draws against 82 / 55 with 8 draws, t0-5tyj); the dear first draws of a run and of a camera cut halve (ter-4iic: 26.3 to 26.5 ms of GPU span at draws 0 and 1 on the card, and the render scale's fall to 0.77 at every start, t58-np60); and a disocclusion under motion shares its block's ray 4 draws sooner.

The user's decision of 2026-11-22: the restart after a light change is "8 draws" for the merge, and the own-ray limit stays 8 now and is tested at 4 later. This ticket is that later test. It does not block the merge of Design X.

Placement: beside ter-4iic under ter-8hyd, not under it. ter-4iic is the ticket of a symptom (the cost of young histories at a start or a cut) and can have other remedies, such as a ray budget. This ticket asks about a constant whose change also moves the evidence's restart and the grain of every young history.

## Design

The model. FullRateFrames is the one constant for "the shortest length that keeps the block's shared ray". It has two readers that must agree: the ray plan's own-ray test and the accumulation's cut on evidence. Change the constant and both move together. Do not add a second constant for one of them, and do not write a rule that knows why a history is young.

What exists. The experiment XC of t0-5tyj (scratch-plan-rate-at-fast-cap.patch in the acceptance record) changed only the plan's test to "at least 3.5 draws" and kept the light restart at FastCap = 4. It removed the cost block after a light change (peak 15.7 ms against 22.8 ms), kept Design X's settle (78 / 51 and 67 / 31 draws) and boiled 0.01 to 0.02 points more in the 60 draws after off (ceiling 0.34 against 0.32 %). The plan reads a stored length of 4.0 as a mean of four taps that can round under 4.0, so the comparison needs a margin or the stored value's own form: settle that in the design.

Not measured, and the reason for this ticket: a start of a run, a camera cut and a disocclusion under motion, where histories of 4 to 7 draws lose their own rays. More grain there is the risk.

The takes, each with a video for the user and the motion measured by its maker: the 18 motion films, the cabin walk, a start of a run, a camera cut, and the fair lamp switch. Sides: the code after the merge with FullRateFrames at 8 and at 4. Measures: the GPU span per draw (peak, draws above the settled span), the settle, the grain per frame, the boil without the fade, the specks, and under the dynamic render scale the scale's fall at a start (t58-np60).

The decision is the user's, by the bar: no blotches, daubs, bright specks, trail or flicker; grain that is there at all is a bug. If 4 passes, the change is the constant, its comment, the docs and red-green tests of both readers.

## Acceptance Criteria

- One table of both lengths over the takes, with a video per take.
- A plain statement per take: 4 draws shows more grain, specks or boil than 8, or it does not.
- The user's decision, recorded with the date. If it is 4: the constant changes with tests, docs and hardening as a ticket of its own or in this one.


## Notes

**2026-11-22T20:27:22Z**

The user's decision of 2026-11-22 (through the orchestrator): the restart after a light change is '8 draws' for the merge (t0-zmjx); the own-ray limit stays 8 now, and this ticket tests 4 draws for every young history after the merge. My decision on its place: beside ter-4iic under ter-8hyd, as a candidate remedy, because the constant also moves the evidence's restart and the grain of every young history. Links: ter-4iic, t0-70v1, t0-zmjx; t58-np60 is linked when the combined check's branch brings it into the line.

**2026-11-22T21:19:42Z**

From the orchestrator, 2026-11-22, after the review of the indirect lighting's approach (docs/research/lighting-system-review-2026-11-22/README.md on main, 45002452b): no new trust rule of the temporal stack while the ray census (ter-8pco) is open. This ticket stays open and is not started. The review's reading: 0.25 diffuse rays per pixel cannot reach the bar by tuning for rare bright sources, and each trust rule trades lag against noise. The structural step is ter-6fka (a diffuse ray that misses the screen, and a young history, read an estimate that the eye's position does not change); its design P would let FullRateFrames and the own rays of young histories go.
