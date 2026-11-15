---
id: t0-jyxv
status: open
deps: []
links: []
created: 2026-11-15T08:36:14Z
type: task
priority: 3
assignee: Alexis Purslane
parent: t7-3e22
---
# The rest of Design X's frame cost on the deck: the reflection rays pass +0.06 ms and the accumulation's evidence read +0.03 ms against main

Found by the frame time cuts (t0-89zi). After those cuts the deck's frame span stands +0.200 ms above main ab54c785e, inside the limit of 0.30 ms. Two passes hold most of the rest in every GPU trace (gpu-trace.sh --no-counters, TERTIUS_DIFFUSE_FILTER=on, scripts/temporal-upscale-timing.json, median busy ms, main then the branch): reflection rays 1.427 -> 1.489, and accumulate indirect diffuse 0.535 -> 0.562. The per-pass spread is about 0.01 ms. Not needed for the merge. Build it only if the frame budget asks for it.


## Design (line orchestrator, 2026-11-15)

This is a measurement ticket with cuts only where a trace shows a cause. The frame time cuts (t0-89zi) found the ray plan's whole rise with one probe trace: the plan with its moved mark set to false cost main's time. Use the same method here.

- Reflection rays, +0.06 ms. The lead of the frame time cuts found no cause in the code: a hit reads the same texel as on main. So take one probe trace per candidate, each with one of Design X's additions to that pass turned off by a scratch edit: the hit's light age code, the hit's moved mark, and any image that the pass binds on the branch and not on main. The candidate whose probe trace returns to main's 1.427 ms is the cause.
- Accumulate indirect diffuse, +0.03 ms. The pass reads the evidence of each block (the sample age image) on the branch. Take one probe trace with the evidence read replaced by a constant. If the pass returns to main's 0.535 ms, the read is the cost.

For a found cause, propose the cut in a note and stop. The line orchestrator decides whether to build it, because a cut can move who owns a fact.

## Acceptance

- A note with one row per probe trace: the pass, the change, the median busy ms, and the clock.
- For each of the two passes: the cause, or the statement that no probe found it.
- No change under `src` in the commit. Probe edits stay in a scratch checkout.
