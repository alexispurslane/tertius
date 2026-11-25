---
id: ter-fl8r
status: open
deps: []
links: [ter-x08a]
created: 2026-11-25T17:53:53Z
type: bug
priority: 2
assignee: Alexis Purslane
---
# Measure glare and isolated bright pixels when looking out of the shuttle into daylight

The moving cabin view on approach to the cockpit still shows a pale veil and isolated bright floor pixels when the launch ramp fills the forward windscreen. Bloom-off controls remove the veil. Camera, exposure and available light matter. Separate diffuse indirect light from specular and compare tracked pixels across frames before changing shared rendering policy. The cabin shot facing the pilot is readable in ter-x08a. Reproduce with the forward walking view, not the seated reverse shot.

## Acceptance Criteria

Separate the pre-bloom shading, diffuse and specular contributions at a fixed viewpoint and exposure. Judge any renderer change on both the cabin and ordinary sunlit materials.
