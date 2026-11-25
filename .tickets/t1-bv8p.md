---
id: t1-bv8p
status: closed
deps: []
links: [t1-6tn2, t1-0q20]
created: 2026-11-23T21:31:06Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, environment]
---
# Prefilter an environment map once per round of its faces: measure whether the prefilter's fixed part, 55 % of today's face on the Mac, falls

**2026-11-25 · Retired.** This optimization was scoped to an earlier GPU workload. Reopen against a named film segment if its pass dominates a repeatable GPU timing there.

Written by the line orchestrator of the back-face line on 2026-11-23, from the split of the environment face's cost (t1-6tn2, docs/research/environment-face-cost/README.md, section 10). No build is ordered. The first step is a measurement.

## Terms

- **Face**: one of the six faces of an environment map, drawn at the eye.
- **Round**: the draws in which every face of a map is drawn once in turn, 6 draws for one map at one face per draw.
- **Prefilter**: the dispatches after a face's light pass (EnvironmentPrefilterPass): the radiance and distance mips, the specular levels, the irradiance downsample and the irradiance convolution, with their barriers.

## The numbers (one face at 128, Mac span / Mac passes / card span, in ms)

- Today's face: 0.696 / 0.631 / 0.174.
- The irradiance convolution: 0.159 (23 %) / 0.182 (29 %) / 0.034 (19 %).
- The rest of the prefilter: 0.230 (33 %) / 0.155 (25 %) / 0.040 (23 %).
- So on the Mac the prefilter is 0.39 ms, 55 % of today's face, and on the card 42 %.

## Two reasons to look at it

1. **Cost.** Today the prefilter runs for each drawn face. On MoltenVK each compute dispatch behind a barrier is a Metal encoder of its own, so part of the 0.39 ms is a fixed cost per dispatch that does not depend on how many faces the dispatch writes. A prefilter that runs once per round, over all six faces of the map in the same dispatches, pays that fixed part once in six draws. How large the fixed part is, is not measured.
2. **Consistency.** The prefilter writes only the drawn face's filters, from the six faces as they are stored then. So after a change the irradiance of a map is right only after two rounds: the first round's faces were filtered while their neighbors were still old. The white room after a cut lasted about 200 draws partly for this reason (t58-rcd6, docs/research/cut-white-room). A prefilter after a full round is consistent after one round.

## The cost of the idea, to weigh in the design

- The irradiance and the specular levels of a map then lag the faces by up to one round, where today they follow each face in its draw.
- One draw in six pays six faces' worth of the part of the prefilter that scales with the texels.
- A draw that refreshes every face (the first draw of a still, a cut that keeps the camera's frame) already prefilters after all six faces, so nothing changes there.

## First step, a measurement with the record's probe

The prefilter of six faces in one batch against six times the prefilter of one face, by the Mac's span and passes and on the card, at 128. If the batch costs about as much as one face, the fixed part dominates and the idea saves up to 0.3 ms per draw on the Mac. If it costs about six times as much, the idea only moves the cost and is dropped for cost, and reason 2 stands alone. Design it twice only after that number.

## Out of scope

The lean field face and the grid of capture points (dropped by the user's word; ter-6fka). The range skip (its own ticket).

