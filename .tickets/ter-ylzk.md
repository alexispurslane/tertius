---
id: ter-ylzk
status: closed
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

**2026-10-31T14:30:21Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 88962f00-4bb7-41be-89dd-bb3951a51fd8), run 3f31a9a4-b26e-438b-9d41-38a4ed919b08, branch ter-ylzk-map-drops-lamp-glow from 148d1251.

**2026-10-31T14:34:26Z**

2026-10-31: implementation. environment_face.frag adds the glow as inMaterialMapAndLight.z == 0u ? s.emissive : vec3(0.0), the same rule and the same mark (GpuInstance.HoldsLight through mesh.vert) as the bounce source in occlusion_depth.comp. No new state, no new output. Tests: EnvironmentFaceTests.AGlowOfAnEntityThatHoldsALightAddsNothingToTheMap (a black glowing panel whose entity holds a lamp behind it is black in face +X) and AGlowWithoutALightOfItsOwnStaysInTheMap (the same panel beside a lamp of another entity keeps its glow). The face texels prove the rule; the irradiance and the reflections derive from them, so no separate irradiance test.

**2026-10-31T14:40:09Z**

2026-10-31: WalkSliceTests.AfterTheQuarterTurnThePortWallFillsTheViewWhereTheRoofWasDark failed with the fix: its bound, roof < wall / 2, encoded the double share. The ceiling panels' glow in the map lit the floor and walls a second time but not the roof, which faces away from them. Without it the auto exposure of walk.json rises about 1.2 stops (cabin 3.62 to 4.81, turned 3.97 to 5.16), and the roof probe goes from (42, 39, 35) to (74, 69, 63), about two thirds of the wall's (109, 103, 95). The bound is now roof < 3 · wall / 4; the roof is still darker than the wall.

**2026-10-31T14:50:52Z**

2026-10-31: run 3f31a9a4 finished the work in cafaab80 (both full runs passed) and failed only its output schema. The orchestrator settled the changed WalkSliceTests bound (roof < 3 * wall / 4) as a consequence of the user's choice. Continuation run a1790cd3-ba6e-4b54-8846-258ccf123496 (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) verifies, reviews and fixes.

**2026-10-31T15:11:36Z**

2026-10-31: review fixes. The rule is one function, glowSent(emissive, holdsLight) in frame.glsl, which environment_face.frag and occlusion_depth.comp both call; frame.glsl is the file that declares GpuInstance.holdsLight, and both shaders already include it. The bounce source now caps min(diffuse + glowSent(...), MaxRadiance) for every pixel; the diffuse image is already at most MaxRadiance (exposed() in direct_pass.glsl), so the result is unchanged. AGlowOfAnEntityThatHoldsALightAddsNothingToTheMap now has a glowing wall of a third entity behind the panel, so it fails (5, 5, 5) if the faces leave light-holding instances out instead of drawing them dark. The rule also darkens the Sun's sphere in every map, because SunRoot holds the sphere and the sunlight (a point light) on one entity; the docs now say so, and that a smooth surface whose reflection SSR misses shows the Sun only through its direct highlight until ter-gf0t.

**2026-10-31T15:17:47Z**

2026-10-31: merged into main. Verification by the orchestrator: the merged code hash 80a30a38 is the hash on which the fixer's full Checked and Release runs passed (all eight test projects). Coverage: the C# changes are doc comments only; the shader lines are covered by EnvironmentFaceTests and BounceLightRenderTests on the GPU. Mutations (fixer's evidence runs): glowSent returning the emission always fails both AGlowOfAnEntityThatHoldsALightAddsNothingToTheMap and ALampsGlowingPanelBouncesOnlyItsLight; skipping light holders in SceneBatcher.BatchFace fails the strengthened face test. The review's medium finding (the Sun's sphere is dark in every map too) is in the docs and was reported to the user.
