---
id: ter-u5be
status: open
deps: []
links: []
created: 2026-10-26T15:01:07Z
type: feature
priority: 4
assignee: Alexis Purslane
tags: [rendering, lights]
---
# A light cutoff that knows the specular peak, so that a far lamp's mirror highlight does not fade early

Found in the review of the lights' Part 4 (ter-iz58), 2026-10-26. The batcher drops a light whose irradiance falls below E_cut, which is set for a matte surface (docs/plans/local-light-shadows.md, Known limits). A mirror's highlight of such a light can reach about 0.5 after exposure, so it fades early over the last fifth of the reach. The window is continuous, so no pixel pops. A cutoff that knows the largest specular peak would keep the highlight longer, at the cost of more kept lights. This changes what the user sees, so the user decides whether and when.

