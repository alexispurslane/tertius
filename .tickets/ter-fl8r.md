---
id: ter-fl8r
status: open
deps: []
links: [ter-x08a]
created: 2026-11-25T17:53:53Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-buzm
---
# Measure glare and isolated bright pixels when looking out of the shuttle into daylight

**Second-cut review, 2026-11-25.** On the second-cut film clock, compare 27.85–28.60 s (seat), 28.60–29.35 s (exterior) and 29.35–30.35 s (pencil). Also measure the longer forward windscreen shot from 5:47.55. Record the initial and settled exposure at the same camera, then isolate bloom and each lighting contribution. Judge hull detail, pilot visibility and the window frames in motion. This incorporates t10-ck8w.

The moving cabin view on approach to the cockpit still shows a pale veil and isolated bright floor pixels when the launch ramp fills the forward windscreen. Bloom-off controls remove the veil. Camera, exposure and available light matter. Separate diffuse indirect light from specular and compare tracked pixels across frames before changing shared rendering policy. The cabin shot facing the pilot is readable in ter-x08a. Reproduce with the forward walking view, not the seated reverse shot.

## Acceptance Criteria

Separate the pre-bloom shading, diffuse and specular contributions at a fixed viewpoint and exposure. Judge any renderer change on both the cabin and ordinary sunlit materials.

## Notes

**2026-11-25T20:09:42Z**

In the second cut on d392c3ae4 the exterior view into the cockpit at 0:28 is heavily overexposed. Use this frame and the preceding interior-to-exterior camera cut as a second reproduction. The cause of this exterior shot still needs measurement.
