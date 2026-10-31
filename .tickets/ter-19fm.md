---
id: ter-19fm
status: open
deps: [ter-ox1a]
links: []
created: 2026-10-31T17:13:54Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, lights, performance]
---
# A spot lamp is in the main view only when its cone reaches the view

Found by ter-ox1a's fixer and the orchestrator (2026-10-31). PreparedLight.InMainView is !camera.Culls(relative, reach) (SceneBatcher.cs:1019), a sphere of the light's reach, and LightUnits.EyeIrradiance, the shadow tiles' ranking value, is intensity over distance squared with no regard to a spot lamp's axis. So a spot lamp that points away from the camera counts as in view: it stays in the direct pass's loop, and it can take a shadow tile from a lamp that lights the floor in view. Example from ter-ox1a: a 1-layer atlas, lamp X 30 m behind the eye aimed away, lamp Z 60 m ahead lighting the floor; one stop brighter, X's reach sphere holds the eye, X's eye irradiance is 4 times Z's, and Z's shadow turns off while Z's light stays. Fix in the one existing flag: a spot lamp's main-view test uses its cone, cut at its reach, against the view frustum; decide with the user whether the tiles' ranking value also accounts for the axis. Expected gains: fewer lamps in the direct pass's loop, and no tile for a lamp whose light cannot show. Test first with the ter-ox1a example scene.


## Notes

**2026-10-31T19:02:31Z**

2026-10-31, the user's choice of two designs: B. (1) The cone test: a spot lamp's PreparedLight.InMainView holds only when its cone, cut at its reach, reaches the view frustum (point lights keep the sphere test). (2) The ranking: a spot lamp in the main view ranks for shadows by its irradiance at the point of its axis that is nearest the eye and inside the view frustum: luminance(I) / max(t^2, R^2, NearestDistance^2), with t the distance along the axis from the lamp to that point, and the axis cut at the reach. Point lights keep the eye irradiance. Rejected: A, the eye irradiance for every light, under which a spot lamp that only clips the edge of the view can outrank one that lights the floor ahead. One ticket, two commits: the cone test first, then the ranking.

**2026-10-31T19:16:07Z**

2026-10-31, settled implementation choice (builder), commit 1, the cone test: CameraView.Culls(apex, cone, reach) tests the cut cone (points within the reach of the lamp and within the outer half-angle of its axis) against the same five planes as the sphere test (near plane, four side planes, no far plane). For each plane it adds reach times the most that a unit step along a cone direction goes into the plane: 1 when the plane's normal lies within the outer half-angle of the axis, else cos(phi - theta), floored at 0. The test is exact per plane and conservative across planes: a cut cone that misses the view near an edge or a corner where two planes meet, yet lies outside neither plane alone, stays in the main view, the same error as the sphere test. A cone of a whole turn culls as the sphere. The faces of the environment map keep the sphere test for every light (out of scope: the ticket names the main view).
