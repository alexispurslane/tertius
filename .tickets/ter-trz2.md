---
id: ter-trz2
status: open
deps: []
links: [ter-kn1d]
created: 2026-11-01T20:34:31Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [demo, geometry, shadows]
---
# Demo geometry: 7 mm open cracks between the wheel room's ceiling and its side walls let the sun in

Found by ter-kn1d's fix stage on 2026-11-01. StationExit.CeilingTurn turns the ceiling bands by half a segment (pi/256) against the side walls, and both rings have 256 flat segments. So at each wall vertex, every 2.33 m along both walls, the ceiling's face lies 7.2 mm inside the wall's top edge, and that gap is open. At tick 3885 of demo-exit the sun is 0.01 to 0.025 below the ceiling's plane and shines through these cracks. In the room's environment map (map 1, face +X) they show as two diagonal stripes on the ceiling, 3.4 m from the eye. One run with CeilingTurn set to 0 removed the step in brightness at tick 3910 on captures/map-flip-2026-11-01/spinward-flip.json (mean display level 96.8 at every tick up to 3925, against 96.5 then 84). This is the only cause of the step that remains after ter-kn1d. It is a modeling defect, not a shadow defect: the fix closes the cracks in the demo's geometry (for example, the ceiling's edge vertices meet the walls' top vertices), and does not change the renderer. Evidence: the ter-kn1d fix stage's report and ticket notes; probe logs in captures/map-flip-2026-11-01/kn1d-fix/ (main checkout, git-ignored).

