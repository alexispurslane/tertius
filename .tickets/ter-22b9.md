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
