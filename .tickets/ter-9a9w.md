---
id: ter-9a9w
status: open
deps: []
links: []
created: 2026-11-03T20:31:30Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [code-quality]
---
# Inspect World, EntityTable and PreparedScene against the TCC and depth goals

CLAUDE.md asks for an inspection of each complete type below a design goal (TCC 1, depth above 2). Measured on main at 5ab0123bb with type-metrics-v3, 965 eligible method pairs share no instance storage. Three types hold 640 of them:

- Tertius.Simulation.World (src/Tertius.Simulation/World.Entities.cs and its other parts): 863 of 1275 pairs share storage, 412 do not.
- Tertius.Simulation.EntityTable (src/Tertius.Simulation/Internal/Entities/): 1399 of 1540, 141 do not.
- Tertius.Rendering.PreparedScene (src/Tertius.Rendering/Scene/PreparedScene.cs): 18 of 105, 87 do not. It is also the type with the most depth drag: 65 API entries over 38 complexity units (depth 0.58).

No earlier ticket judged these types. The metric alone does not show a defect: a facade like World can be coherent with a low TCC.

## Acceptance Criteria

For each of the three types, read the type and its callers, and record one of two results in this ticket: a concrete refactoring with its evidence (exposed knowledge, repeated coordination or change amplification), filed as its own ticket, or a one-paragraph reason why its role justifies the score. Name the method groups that share no state.

