---
id: ter-x08a
status: closed
deps: []
links: [ter-fl8r]
created: 2026-11-25T17:26:49Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-qdn8
---
# Keep the fitted shuttle cabin readable under daylight

The settled cabin camera at demo-surface tick 1365 shows a white veil across the forward aisle and many white points across the ceiling and floor. A 135-frame capture holds exposure at +2.52 stops. Switching bloom off makes the aisle legible, and the white points persist. Isolate material, lighting and camera contributions with matched controls. Correct the cause in the fitted cabin or shot. Preserve the clear exterior windows, authored normals and physically credible lighting.

## Acceptance Criteria

Show a matched moving interior with legible forward detail, without compromising the view from outside. Record which controls explain the white points and the veil. Preserve independent checks for the shared cabin openings and surface relief.


## Notes

**2026-11-25T17:27:30Z**

The fitted shuttle owner is testing the production cabin in the existing art checkout. T3 task lookdev-cabin-daylight-after-fit. Matched 135-frame runs with bloom on and off use the cabin camera at tick 1365. Both measured +2.52 stops after settling.

**2026-11-25T17:54:05Z**

The five added ceiling panels, wider wall lamps, matte cabin finishes and reverse angle change the seated daylight shot. Forward-looking shots through the lit nose windshield can still show glare and isolated bright floor pixels. ter-fl8r records the shared rendering investigation.
