---
id: ter-uyi3
status: closed
deps: []
links: []
created: 2026-10-26T22:20:03Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering]
---
# TransparentPass leaks the pipelines that it made when a later pipeline fails to build

Found by the review of skin and hair Parts 1 to 3 (2026-10-26). TransparentPass's constructor makes the glass, fringe and puff pipelines in a row; if a later one throws, the earlier ones are never disposed. GeometryPass, ImpostorPass, EnvironmentFacePass and ShadowPass.CreatePipelines got try/catch blocks that dispose what they made. The glass-before-puff leak already exists on main. The fixer dropped it because no test can make only one pipeline's creation throw without a test seam in ShaderLibrary. Mirror the other passes' try/catch, and add the seam if the project wants the test.


## Notes

**2026-10-27T03:19:37Z**

2026-10-26: merged. The puff pipeline's creation destroys the glass pipeline before it rethrows, as ImpostorPass and EnvironmentFacePass do. No test: both pipelines take the same inputs, so no input fails only the second without a test-only seam in production code.
