---
id: ter-jxjx
status: closed
deps: []
links: []
created: 2026-11-04T18:20:05Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
---
# The screen tracer's design after the spikes: V6, cap 62, full rays, the 12 ms budget

Design prerequisite of ter-gayt (build ticket 4). docs/plans/temporal-renderer.md sections 6, 11 and 12 and the decisions that ter-gayt names predate the lean trace spike (ter-8hbi), the profiling spike (ter-sftw), the GPU calibration (ter-3ug8), the removal of light sampling (ter-9nrc) and the user's 12 ms target. One design agent updates the plan with the settled results and writes ter-gayt's order of work: the walk (V6's two-depth step for both kinds in one walk and one kernel, over DepthPyramid with its near channel and a finest level), the caps (reflection 256, diffuse 62), 10 % own rays, the record read in the words form, the budget under 12.0 ms (2.94 to 3.76 ms for the two dispatches on the deck, from ter-gayt's note of 2026-11-04), and the user's rule for the first build: full rays, measure the real frame at the top clock, add a ray limit only if the frame stays over 12 ms after the hill-climb (ter-dzxf). No code.


## Notes

**2026-11-04T18:20:30Z**

2026-11-04: started as a design delegate (Opus, xhigh) in its own worktree from 50086024d, request ter-jxjx-design-2026-11-04-a. The orchestrator inspects and merges the plan update, then ter-gayt starts.

**2026-11-04T18:45:19Z**

2026-11-04, the design update of docs/plans/temporal-renderer.md. ter-gayt's builder can work from the plan alone. No code changed. What changed:

- Terms: walk, step cap, two-depth walk, finest level, own-ray share, top clock and tile bias.
- Decisions 40 to 46, in a new part "Added on 2026-11-04":
  - 40: the two-depth walk (V6) for both kinds, in one kernel.
  - 41: the caps, 256 steps for reflection rays and 62 for diffuse rays.
  - 42: the 12.0 ms budget. The controller's limit stays 15.0 ms.
  - 43: full rays at the first build, and the rule for a later ray limit.
  - 44: the words form. loop-reads.py counts 0 loop reads.
  - 45: the walk's one place, `walkScreen` in `screen_walk.glsl`.
  - 46: the finest level in an R32F image of its own.
- Decisions 4, 9 and 25 point to the new ones, and the settings row holds `ScreenTrace(0.5, 256, 62)`. Each changed item gives its old value and the date of the change.
- Section 2: the depth pyramid's step adds the finest level's dispatch. The screen trace records four passes, after the direct light's images. The graph holds 27 passes.
- Section 6: the traversal is the two-depth walk. The section gives its facing test, hair rule, image bias, caps and kernel. It lists the candidates outside the first build. It also covers the own-ray share, the frames after a reset and one own-ray list per kind.
- Section 8: the usage code and the interface of `TemporalSignal` match main after ter-9nrc (`ReadSet`, `HitSet(parity)`, `Accumulate(context)`).
- Section 10: the tracer's tests add the walk against a flat-slab march, the cap, the facing test, the hair card and the finest level.
- Section 11: the budget is 12.0 ms. A ledger of the frame without the trace gives the two dispatches 2.94 to 3.76 ms. Each number says measured, estimated or allowance, with its source. The first version's 15.0 ms, 1.7 ms and 1.1 ms figures stay as a record.
- Section 12: the deletions and the docs rows follow the walk.
- Section 14: the diagram adds the spikes, this ticket, the light-change ticket ter-jz2y and the hill-climb ter-dzxf. Acceptance 4 follows decision 43. A new part "The parts of ticket 4" gives eight parts in five stages, with files, interfaces and finish tests. A new part "Open forks of ticket 4" holds two forks for the user.
- The budget report of 2026-11-04 is now docs/research/temporal-renderer/trace-budget-12ms.md, with its punctuation changed to the writing rules.

Open forks, for the user before ticket 4's part 4 and part 1:

1. How ticket 4 measures at the top clock: a busy headless film with two frames in flight, or a window run that writes its GPU timings.
2. Whether the walk gets a C# copy with CPU tests, or no copy and GPU tests against a flat-slab march in test code.

**2026-11-04T18:46:43Z**

2026-11-04: merged into main as f244ec0c4 after the orchestrator's reading. The two open forks of ticket 4 (how to measure the frame at the top clock, how to test the walk) go to the user before ter-gayt starts.
