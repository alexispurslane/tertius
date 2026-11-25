---
id: ter-22b9
status: in_progress
deps: []
links: []
created: 2026-11-25T15:25:33Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-qdn8
---
# Dock inside the Pioneer hub and ride a working enclosed elevator through its spoke

Select a real dock opening from the converted mesh. Make navigation, collision, visible floor and furnishings agree. Replace the placeholder bay, remove coincident faces, build the elevator and traversable furnished rim arrival in the same spaces. Verify grain vs geometry and reflected stability.


The pinned Pioneer docking file places berth 14 at native (-104.6094, 0.0822, -3.3508) m. In the converted hub, four floor triangles lie at x = -104.611 m, span station y = -78.99 to 99.76 m and z = -20.81 to 20.81 m. At the lift center (station y = 0, z = 3.8), radial rays also hit the back of the hub at x = -157.867 and the rim at -1725.515 m. The former painted bay box drew an unbroken face across the deck surface and lift hole. Removing the painted box exposed the source interior. The art scene regression failed before that change and all five art scene checks passed afterward.

Station conversion cuts a 4.10 m square through berth 14 and a 15 m square through the hub and rim skins inside the spoke. The cooked GLB test first failed at x = -104.6106 m. After recooking only the station, nine rays from inside the car envelope pass beyond x = -2000 m and five rays beside the opening still hit the original deck within 2 mm. The mesh edits preserve the outer rim.

The berth slab now covers the imported floor out to its edges. The kinematic car has a roof, windows, handrail and a front doorway. Its departure and arrival operate physical sliding landing gates. The rim waiting room shares the existing curved wheel floor. Both the berth masts and the waiting room walls have static collision geometry. The local model adds transit signs on the lintels and waiting room wall. The shuttle envelope, parked axial and lateral position, spin rate and wheel radius stay at their previous values. The radial position moves 2.411 m to meet the source floor.

The Checked geometry tests reproduced the 2.411 m wheel/deck disagreement, missing cabin roof, missing rim gate and missing waiting room wall. The five-wall fallback test also reproduced the duplicate deck face. Source geometry and contact tests passed after these changes. The camera inside the car now clears the roof. The film scripts use the tick counts from a new simulation rehearsal.

The synchronized ride trace samples the signed height of the rider's feet above the car floor at 60 Hz. At cruise (20 m/s, 4,148 samples), the old feet stayed 333.206–333.332 mm above the car. Updating a rider after its retreating kinematic floor moves keeps cruise within 0.030 mm. When a hand-off replaces the ground body, refreshing the stale contact also removes the one-tick 35.4 mm gap. Across all 7,846 samples the greatest absolute height error is 0.403 mm. The Checked disembark suite passed all 21 tests, including the whole ride and walking into the terminal.
