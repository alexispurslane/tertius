---
id: ter-0i2s
status: closed
deps: []
links: []
created: 2026-11-25T15:25:33Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-qdn8
---
# Find and fix the blue screen wash and expanding boarding circles

Isolate both symptoms against the delivered movie and deterministic captures. Test cameras, overlays, geometry and postprocessing separately before modifying the responsible path. Check true interior views as well as the diagnostic shots.


### Captures of the first cut

Times include the five-second opening card. Each control uses the original camera and simulation tick unless the row names a camera change. The controlled captures use converted art, FSR 3 at two-thirds scale and 480 × 270 output.

| Time and shot | Controlled change | Result |
|---|---|---|
| 00:16.23 pad | Lower the drawn globe by reducing its radius 1 m. | The expanding blue-white discs disappear across 229 matched frames. Before the fix the pole reached the walking surface at 6,371,000 m. The single-precision vertex spacing there is 0.5 m and the launch pad rises just 0.02 m. The discs persist in independent controls with bloom, screen tracing, environment maps, cascaded shadows and upscaling disabled in turn. |
| 00:20.38 boarding | Turn off frame outlines. | The rings disappear. 4,870 of 129,600 pixels change by over 8 sRGB codes in at least one channel. The same two runs give identical seated and climb-cabin control frames. |
| 04:00.00 glide-rim | Disable outlines, then disable bloom. | The upper-left sky pixel at (25, 30) stays RGB (55, 88, 159) with bloom on and off. The Sun loses its halo when bloom is off. |
| 04:00.00 glide-rim | Move the eye radially inward from 1,998 m to 1,700 m, keeping the hub aim point. | The sky pixel becomes (0, 0, 0) with bloom on. A triangle intersection against the cooked station places the first ring_glass surface 264.0 m down the old center ray. The nearest glass vertices lie 1,731.9 m from the axis. |
| 05:54.16 entry, shuttle window | Degenerate only ring_glass triangles in a private copy of the station model. | The hub patch still averages RGB (81.6, 94.2, 127.2), measured at x = 322–348, y = 106–160. |
| 05:54.16 entry, shuttle window | Set the canopy’s diffuse opacity to zero. Keep its dielectric coating and specular reflections. | The same patch averages (96.2, 100.8, 100.2). The center pillar is visible in both frames. The hull and cabin fit belongs to ter-qdn8. |

The short diagnostic script uses the existing outlines step. The cinematic generator and its checked-in scripts leave it off. Film names, route events, capture cadence and tick counts are unchanged.

### Regression checks

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| F1 | src/Tertius.Demo/BlueDanube.cs:236 | Show outlines in boarding | killed | BlueDanubeEditTests.TheCinematicFilmKeepsFrameOutlinesOff | 4.7 |
| F2 | src/Tertius.Demo/BlueDanube.cs:278 | Show outlines in climb-low | killed | BlueDanubeEditTests.TheCinematicFilmKeepsFrameOutlinesOff | 4.4 |
| F3 | src/Tertius.Demo/BlueDanube.cs:286 | Show outlines in climb-high | killed | BlueDanubeEditTests.TheCinematicFilmKeepsFrameOutlinesOff | 4.2 |
| F4 | src/Tertius.Demo/BlueDanube.cs:292 | Show outlines in orbit | killed | BlueDanubeEditTests.TheCinematicFilmKeepsFrameOutlinesOff | 4.5 |
| F5 | src/Tertius.Demo/BlueDanube.cs:393 | Show outlines in glide-rim | killed | BlueDanubeEditTests.TheCinematicFilmKeepsFrameOutlinesOff | 4.6 |
| F6 | src/Tertius.Demo/BlueDanube.cs:452 | Show outlines in entry | killed | BlueDanubeEditTests.TheCinematicFilmKeepsFrameOutlinesOff | 4.3 |
| R1 | src/Tertius.Demo/DemoScenes.DemoStation.cs:759 | Restore the eye behind the ring glazing | killed | DemoStationTests.TheRimCameraLooksAtTheHubFromClearOfTheRingsInnerGlazing | 7.5 |
| G1 | src/Tertius.Demo/DemoScenes.Shuttle.cs:677 | Restore the diffuse blue canopy | killed | ShuttleTests.EachPaneIsGlassThatCastsNoShadow | 5.4 |
| P1 | src/Tertius.Demo/DemoScenes.cs:855 | Draw the Earth at walking height | killed | BlueDanubeArtSceneTests.ThePlanetMeshStaysBelowTheBoardingSlab | 4.9 |

The pad clip compares all 229 consecutive frames at 60 fps. The rim clip compares 80 samples spanning the original 13.33 seconds. The shuttle is small and its shaded side is faint at the ends of the rim view. The long-run test projects both the shuttle and hub into every glide-rim frame. The branch predates the new berth alignment.
