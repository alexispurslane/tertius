---
id: ter-19fm
status: in_progress
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

**2026-10-31T19:03:28Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 869b0b07-3413-4b06-8707-a88a91bf6e91), run e1eaacb5-1045-445f-8b16-712c448dc38d, branch ter-19fm-spot-cone-in-view from eddfe104.

**2026-10-31T20:01:53Z**

2026-10-31: run e1eaacb5 committed the cone test (e9c8b993) and stopped at a fork in design B's ranking: with t the lamp's distance along its axis to the in-view axis point nearest the eye, a lamp that points away from the eye has t = 0 and ranks at the top at any distance, so in lamp-hall-16-spot with 4 tiles the four farthest lamps (15.7 to 21.3 m, 12.3 % of the floor light) took the tiles from lamps 6.6 to 10.8 m away (43.9 %). The user's choice of two designs (eye to beam, eye to lamp): eye to beam. A spot lamp in the main view ranks by luminance(I) / max(D², R², NearestDistance²), with D the eye's distance to the in-view axis point nearest the eye; where its cone reaches the view but its axis misses it, D is its reach. Point lights keep D = the eye's distance to the lamp, so one rule covers every light: a spot of light D from the eye shows on screen in proportion to 1/D². Rejected: eye to lamp for every light (no ranking code), under which a lamp whose beam only clips the view's edge can take a tile from one that lights the floor ahead.
