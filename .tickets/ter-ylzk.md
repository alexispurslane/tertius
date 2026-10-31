---
id: ter-ylzk
status: in_progress
deps: []
links: []
created: 2026-10-27T15:10:52Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, lights]
---
# The environment map counts a lamp's power twice: its faces add the glow of an entity that holds a light

Found by the review of ter-vtc7 (2026-10-27). The user's rule: an entity sends its power once; an entity that holds a light sends it through that light, and its glowing surface is the face of that light. ter-vtc7 applies it to the bounce light's source. environment_face.frag still adds the emission of every instance, so a light-holding entity's glow goes into the map's irradiance, which lights the room as ambient light on top of the lamp itself. ARCHITECTURE.md and docs/plans/screen-space-illumination.md (Known limits) record it.

Options, for the user:
(a) Keep it as a known limit.
(b) Drop the glow in the faces where the instance holds a light (one line in the face shader). The lamp panel then shows dark in the map's reflections.
(c) Leave the glow out of the irradiance only (a second output of the faces, or an irradiance-only path), so the reflections keep the panel. Costs memory and a change to the faces and the prefilter.


## Notes

**2026-10-31T14:29:56Z**

2026-10-31: the user chose option (b). The face pass drops the glow of every instance that holds a light, so the map's irradiance and its reflections carry no glow of a light-holding entity. The user accepted that the lamp's panel shows dark in the map's reflections, also where a reflected panel leaves the screen and screen-space reflections hand over to the map. Option (c), which kept the panel in the reflections with a second face output, was declined.

**2026-10-31T14:34:26Z**

2026-10-31: implementation. environment_face.frag adds the glow as inMaterialMapAndLight.z == 0u ? s.emissive : vec3(0.0), the same rule and the same mark (GpuInstance.HoldsLight through mesh.vert) as the bounce source in occlusion_depth.comp. No new state, no new output. Tests: EnvironmentFaceTests.AGlowOfAnEntityThatHoldsALightAddsNothingToTheMap (a black glowing panel whose entity holds a lamp behind it is black in face +X) and AGlowWithoutALightOfItsOwnStaysInTheMap (the same panel beside a lamp of another entity keeps its glow). The face texels prove the rule; the irradiance and the reflections derive from them, so no separate irradiance test.

**2026-10-31T14:40:09Z**

2026-10-31: WalkSliceTests.AfterTheQuarterTurnThePortWallFillsTheViewWhereTheRoofWasDark failed with the fix: its bound, roof < wall / 2, encoded the double share. The ceiling panels' glow in the map lit the floor and walls a second time but not the roof, which faces away from them. Without it the auto exposure of walk.json rises about 1.2 stops (cabin 3.62 to 4.81, turned 3.97 to 5.16), and the roof probe goes from (42, 39, 35) to (74, 69, 63), about two thirds of the wall's (109, 103, 95). The bound is now roof < 3 · wall / 4; the roof is still darker than the wall.
