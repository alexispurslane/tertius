---
id: t1-kiaw
status: closed
deps: []
links: []
created: 2026-11-15T01:24:22Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-kh4g
tags: [rendering, performance, design]
---
# The back-face pass repeats the vertex work of every one-sided solid mesh: 0.45 ms on the RTX 2000 Ada and 0.80 ms on the M1 Max in a crowd of 2.0 million skinned vertices

Found in stage 1b (docs/research/back-face-depth-layer/stage-1b/cost.md). In rooms the pass alone costs 0.032 to 0.062 ms on the card. In scripts/ter-kh4g-crowd-timing.json (rigs-crowd, 100 skinned cylinders of 20,202 vertices) it costs 0.453 ms of the card's frame and 0.801 ms of the Mac's. Its own time on the card is 0.485 ms, against 1.294 ms for the G-buffer pass. So the pass's cost follows the vertex count. The design of stage 2 must say what a view with many skinned figures pays. It must also say whether the pass can share the vertex work of the G-buffer pass or skip small or far meshes. One person has about 20,000 vertices, so the crowd is far denser than a usual view.

## Design of the experiment (stage 2, 2026-11-15)

This ticket is an experiment of stage 2, with no code for the branch. It uses the probe and the method of `docs/research/back-face-depth-layer/stage-1b/cost.md`. Read it first, with `stage-1b/probe.md`. The parent agent does not accept the crowd's number for Alexis as it stands. So this ticket brings a measured answer: a cost model, the vertex counts of real views, and the measured effect of each remedy.

### What is known

- The deformation runs once per frame in a compute pass, and every pass draws the deformed copies (ARCHITECTURE.md, "Skinning and morph targets"). So the back-face pass does not skin again. It transforms and rasters every vertex of the one-sided solid runs a second time, with positions only.
- The crowd holds 2.0 million vertices. There the pass's own time on the RTX 2000 Ada is 0.485 ms, and the G-buffer pass takes 1.294 ms.

### Part 1: the cost model

1. Split the pass's time in the crowd into vertex work and fragment work, on both machines. One way is a run with a scissor of one pixel, which keeps the vertex work and drops the fragments.
2. Give the pass's cost per million drawn vertices on each machine, with its spread.
3. Count the vertices that the pass draws in real views: the deck and the cabin of `demo-exit`, `rect-lamps`, `demo-docking`, `station`, and a view with one person and with ten persons. State where each count comes from.
4. From 2 and 3, state what each real view pays, and compare it with its measured cost where stage 1b measured it.

### Part 2: the remedies

Measure each remedy in the crowd and in the cabin, on both machines, against the pass as it is. A remedy that changes which pixels get a back face also needs its count of changed pixels and a still.

| Remedy | What it changes | The reason that must hold |
|---|---|---|
| The pass draws both faces of every run in one pipeline, and the fragment shader drops the front faces of closed one-sided instances | Pipelines and culling | The cards ticket `t1-yy2f` needs both faces for two-sided and open meshes. This measures what one pipeline costs against culled draws. |
| The pass leaves out an instance whose thickness cannot change the walk | Fewer vertices | A reason from geometry, not a tuned size. One candidate: the instance's bound is thinner along the view ray than the plane span of its pixels, so its slab is a sheet's already. |
| The pass draws a coarser copy of a mesh | Fewer vertices | The engine must hold such a copy today. If it holds none, say so and do not build one. |
| The pass leaves out every instance that an impostor already replaces | Fewer vertices | An impostor is a card, and a card is a sheet. Say whether the pass draws such instances today. |
| Another remedy that you find | Yours | Its reason |

Do not tune a constant to make the crowd cheap in the measurement. A remedy needs a reason that a reader can check.

### Part 3: what remains

State the pass's cost in the crowd and in the rooms after the remedies that have a reason. If a real trade remains, write it as a question with its options: what Alexis gets, and what it costs in ms on each machine.

### Settled decisions

- The probe's changes stay throwaway: patches, scratch commits, and the folder `docs/research/back-face-depth-layer/stage-2/`. The branch gets documents, scripts and small tools only.
- The timing method is stage 1b's: interleaved rounds in a balanced order, and one exclusive hold of the GPU lock per scene. A hold lasts at most about 20 minutes. Each run has a fixed exposure and a recorded GPU clock, and each side has at least five kept rounds. In the cloud, warm every tree once before the rounds (bug ticket `t1-s5c1`).
- The cloud budget of this ticket is $0.80 by the job log's estimates.

### Deliverables

- `docs/research/back-face-depth-layer/stage-2/crowd-cost.md`: the cost model, the counts, and the table of remedies with main and the spread. It also holds the remaining trade as a question, the commands, and what was not measured.
- The patches and the scratch refs.

## Acceptance Criteria

The design document states the pass's cost model by vertex count and its answer for views with many skinned figures.


## Notes

**2026-11-15T13:12:31Z**

Result (2026-11-15, merged at f0e9dc1f1, record docs/research/back-face-depth-layer/stage-2/crowd-cost.md). The pass's cost is a straight line in drawn vertices: 0.24 ms per million on the RTX 2000 Ada and 0.36 ms per million on the M1 Max. The demo's rooms draw 100,000 to 125,000 vertices and pay 0.04 to 0.065 ms on the card. One figure of 20,000 vertices pays 0.005 ms on the card and 0.007 ms on the Mac. No remedy with a reason makes the crowd cheaper. The design's decision 8 holds the model. The trade for many skinned figures goes to Alexis as a question with three options. Cloud dollars: 0.15 of 0.80.
