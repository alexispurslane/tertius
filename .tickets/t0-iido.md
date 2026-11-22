---
id: t0-iido
status: open
deps: [t0-5tyj, t0-3vwx, t0-zmjx]
links: []
created: 2026-11-12T13:56:24Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Harden Design A, update ARCHITECTURE.md and merge t7-3e22-retained-deviation

Design: docs/plans/bounce-light-confidence.md, section 22. Branch t7-3e22-retained-deviation. Step 9. Merges ter-18hc and ter-wkvw with Design A in one merge (user, 2026-11-12).


## Notes

**2026-11-13T01:01:22Z**

2026-11-12: now step 18 of the rewritten design (Design X), after step 17 (t0-5tyj). The built steps t0-ylwm, t0-hfo4, t0-q6jl, t0-anp3 and t0-f7zv close with this merge. ARCHITECTURE.md: the light-age record's codes and the moved mark, the accumulation and its evidence, the diffuse filter, the storage image counts.

**2026-11-22T15:09:21Z**

Decision of the user, 2026-11-22, on the remedy under a moving render scale, in the user's words: "I want videos of the walk and lamp switch under normal engine conditions — that is, dynamic render scaling — because that's always on (or should be). When we merge Design X, we should *not* hold render size, we should let it scale as before, and yes, we'll get main-like behavior, but we'll have a platform of better logic to work off of, to try to improve". The orchestrator's reading, which binds this line: (1) Design X merges with the render scale controller as it is. No hold of the render size, not before the merge and not as a planned fix after it. My design H (the held scale) is rejected. (2) The user accepts that Design X behaves about like main while the render size moves. (3) How the region's drift test and a moving render size fit together stays open as later work on top of the merged Design X, in t4f-71qp. No design and no build now. A test that survives a size change is one candidate for then, not a decision. (4) The merge itself still waits for the videos, the rest of the acceptance record and the combined check.

**2026-11-22T16:23:27Z**

The merge decision waits, by the orchestrator's word of 2026-11-22, for two results: the cause of Design X's bright speckled window jambs at a render scale near 1 (measured on the Mac at a constant scale, with the fair lamp switch), and the dynamic-scale videos taken again after the wobble's fix is on main (the projection's aspect from the output extent, built by the back-face line).

**2026-11-22T20:53:26Z**

The merge decision is the orchestrator's since 2026-11-22, by the user's hand-over, on these gates: (1) the cost variant built with red-green tests (t0-zmjx); (2) tc-8142 fixed; (3) Checked and Release green on the merged code; (4) the per-scene table with no scene worse than main by the user's bar; (5) the two films under the dynamic render scale on the final code, with their motion measured (t0-3vwx). When all hold, the line orchestrator tells the orchestrator, who does the last read of the diff and says go. The user sees the films afterwards. The direction stands: Design X is "strictly better", it merges without holding the render size, and it is "a platform of better logic to work off of".

For gate 5 the line orchestrator reversed its answer 55 (the walk is not taken again): both films are taken again on the final code, each with a new scale log from the card. The combined check's lead has the notice.
