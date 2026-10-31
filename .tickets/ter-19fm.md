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

**2026-10-31T20:02:19Z**

2026-10-31: continuation run 12ab987c-c076-4377-9bf1-f2ca1606142b (version 869b0b07-3413-4b06-8707-a88a91bf6e91) applies the saved ranking patch with the eye-to-beam distance, then runs the review and the fix.

**2026-10-31T20:41:24Z**

Settled by the orchestrator from the user's decisions (2026-10-31), after review 12ab987c: a spot lamp whose cone reaches the main view but whose axis misses the view ranks for shadows in its own tier, below every main-view light whose beam (spot lamp) or position (point light) is in view, and above every light that only a face keeps. Inside that tier it ranks by its light at its reach, luminance(I)/reach^2, ties in list order. ShadowTiles and FarShadows both use this one tier order. Reason: the user's design B says that such a lamp has 'the lowest rank among lights in the main view', and the user rejected design A because a lamp whose beam only clips the view's edge can take a tile from one that lights the floor ahead. The rank at its reach alone (D = reach) does not give the lowest rank: a lamp at (0, 3, -40) aimed down with a 25 m reach ranks at 0.39 cutoffs, below an edge lamp at 1.0 cutoff (the reviewer's scene). RankDistance = +infinity was rejected, because then FarShadows' Share threshold never gives such a far lamp a slot. The user can override this choice.

**2026-10-31T20:42:58Z**

Continuation run 2aca1e4d-0477-4cd2-81df-ae135c458c38 (version 869b0b07) started on the preserved worktree at f70e3e49, with the tier rule and the review's four low findings. Run 12ab987c stopped in its fix stage, with cleanup complete and no change to the checkout.

**2026-10-31T21:41:19Z**

Run 2aca1e4d: build (tier rule f9e9bfd0, low findings dda9a4e0) and review done; the review asked for 2 medium and 4 low fixes (a boxing tier comparison on a per-frame path, no test of the rank inside tier 2, an unenforced cache in CameraView, three doc items). The orchestrator stopped its fix stage at its start, cleanup complete, checkout clean at dda9a4e0, because it made a fifth builder. A continuation with those findings starts when a slot frees (brief /tmp/briefs/19fm-cont2.txt).

**2026-10-31T22:44:52Z**

Continuation run c106152e-8e5f-42cb-bb45-dac03d7cde4d (version 869b0b07) attaches the worktree at dda9a4e0 to fix the six findings of review run 2aca1e4d, with the base eddfe104 unchanged.
