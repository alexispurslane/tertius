---
id: ter-6vhj
status: closed
deps: [ter-nl5u, ter-a24f, ter-s9cv]
links: []
created: 2026-10-25T12:01:26Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, frames]
---
# Atmosphere color per semantic frame: the sky backdrop, and a tint over the view where the atmosphere calls for it

The user asked on 2026-10-25 for an atmosphere color that each semantic frame determines, for Mars-like planets and others in the future. Two uses:
- The sky backdrop. Today the sky is a procedural star field, fixed to the axes of the root frame of the camera's frame tree, black between the stars. Inside an atmosphere, the sky shows the atmosphere's color (blue for Earth, butterscotch for Mars), and the stars fade.
- In some cases, a tint over everything: for example the color of the light through a dusty atmosphere, or haze that grows with distance.

The frame's MediumField already describes an atmosphere's density for drag and lift, so the color can live with it or beside it. The sky color of the hemisphere ambient light (the ticket on per-frame sky and ground colors) can follow from the atmosphere's color.

Design to decide (two designs, then the user chooses):
- Which frame's atmosphere sets the sky: the camera's semantic frame, or the frame whose atmosphere the camera is inside, by the medium's extent.
- How the sky fades from the atmosphere's color to black with altitude.
- Whether the tint is the sun's color after its path through the atmosphere, a haze by distance in the lighting pass, or both, and which cases turn it on.
- How it meets exposure and the tone curve of the rendering epic.
- What the render snapshot carries for it.

## Acceptance Criteria

A Mars-like test frame shows a colored sky behind its ground, with the stars faded, and the space above it stays black. The tint shows where the design turns it on. The look is reviewed in headless screenshots, and ARCHITECTURE.md describes the rule.


## Notes

**2026-10-25T21:55:49Z**

From the environment map (ter-nl5u, docs/plans/environment-map.md, "The atmosphere"): the sky color reaches the map through one function, skyRadiance(direction) in sky.glsl, which the lighting pass adds at a sky pixel and the sky pass draws behind each face of the map. It returns black until this ticket. Change its body, and the map carries the sky's color into the ambient light, diffuse and specular, with no change to the map. A dimming of the stars, by the atmosphere's transmittance or brightness, goes into the star code that the sky pixels (starRadiance) and the reflected stars (reflectedStars, starMeanRadiance) share, so that a mirror and the sky dim the stars the same way. The open-sky share keeps its meaning: haze does not make a texel less open. A tint of what lies in the air must apply in the face shader (environment_face.frag) too, through the same function as in the main view.

**2026-10-31T14:57:51Z**

2026-10-31: the user chose the painted design (B) on all three points, over the scattering design (A, Hillaire 2020, recommended for the rule 'prefer the real mechanism'):
- Sky: a painted sky radiance (RGB, SI, W/(m²·sr)) on PlanetMedium, the full-day value at the surface. Its brightness follows two explicit rules: times a day factor from the Sun's elevation (dark at night), and times the air's density share at the eye (density at the eye's height over the surface density, so space above stays black).
- Haze: a painted fog color and a distance per planet, switched on per frame (no haze when the planet sets none). It applies in every pass that shades a surface seen through the air, the environment faces included, through one shared function (see the 2026-10-25 note).
- Sunlight: a fixed painted tint per planet on the Sun's light, for what lies in the air. It does not change with the time of day.
Shared by both designs, settled: the sky comes from the nearest frame on the camera's chain that has a planet medium, at the eye's height in it; the stars fade by day through auto exposure, with no star rule; the color reaches the environment maps through skyRadiance; the tone curve does not change; the render snapshot carries one atmosphere record (the planet's center relative to the camera, its parameters, and which light is its sun).
Left to the builder by the rules (a choice that changes the look is a fork): the day factor's curve, how the haze and the tint fade with the density share, how the sun light is chosen (deterministic), and whether the sky has a horizon gradient (default: none). The hemisphere ambient (map off) stays as it is. The builder writes the settled rules into docs/plans/atmosphere.md first, then implements. Waits for a free builder slot.

**2026-10-31T15:20:49Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6), run ef4422b3-0f71-472c-a5e7-33554b1f6ade, branch ter-6vhj-painted-atmosphere from 3d1a8c42.

**2026-10-31T15:29:17Z**

Design doc: docs/plans/atmosphere.md holds the settled rules, the types, the formulas with units, the snapshot record, and the builder's choices: the day factor smoothstep(-6°, +6°) of the sun's elevation; the haze's optical depth from the mean density share along the ray; the tint mix(1, SunTint, s(p)) at the lit point; the sun as the point light with the most irradiance at the planet's center, P (r+g+b) / max(d², R²), ties to the earlier light; no horizon gradient.

**2026-10-31T16:09:55Z**

Implementation choices (builder, 2026-10-31): the haze runs in a new haze pass (HazePass, haze.frag) after the reflections and before the transparent pass, through the dual-source blend that glass already uses, and records nothing without a haze. The panes, fringes and puffs apply the same throughAir rule with their cover (1 − t, the coverage, 0), and environment_face.frag applies it in plain radiance. The sun's GPU light carries GpuLight.airSun, and lightIrradiance applies the tint there, so every pass that adds a light's irradiance takes it. The sky and the haze light are capped at MaxRadiance on the CPU (PaintedAtmosphere.Prepare). The demo's Earth air keeps no look, so demo-surface and every reference image are unchanged; the new mars-air scene and scripts/mars-air.json show the look.

**2026-10-31T16:09:55Z**

Cost (Metal System Trace, 40 frames of mars-air's day view, 1920 x 1080, Release, M1 Max): haze pass 0.126 ms; direct light 1.762 ms against 1.716 ms without the look; environment faces 0.126 against 0.128; ambient light 0.540 against 0.529; exposure 0.126 against 0.069, not explained; frame span 9.685 against 9.334 ms.

**2026-10-31T17:11:06Z**

Review fixes (builder, 2026-10-31): (1) The haze light takes the day factor at each hazed point, D(e(p)) with up = (p - c) / |p - c|, not at the eye: GpuAtmosphere gained sun (the sun's place relative to the eye, w 1 with a sun) and dayFactorAt in atmosphere.glsl applies it in throughAir. Reason: with D at the eye, night ground glowed with a daylit eye's haze, and the day side went dark from a night eye. The sky keeps D at the eye, as settled. (2) The eye's height and density share come from the CPU once per frame (GpuAtmosphere.eye), and no pixel computes them again. (3) A screen-space reflection's hit takes throughAir with its coverage as the cover (reflection_ray.glsl), so the traced and the map reflections show one haze. (4) An impostor's light signature multiplies the sun's irradiance by PaintedAtmosphere.SunTintAt at the instance's center, the C# copy of sunTintAt; the batcher prepares the atmosphere with the camera (PreparedScene.Atmosphere) so the impostors can read it. (5) GpuAtmosphere.HasHaze holds the C# side of the no-haze encoding. Open fork for the user: the ground's haze and tint pop off at any bubble edge (T about 0.48 at mars-air's 300 km edge); docs/plans/atmosphere.md, 'Not in this ticket', says so, and the mars-air views edge-inside-view and edge-outside-view show it.

**2026-10-31T18:00:14Z**

2026-10-31, integration: the fix stage (run ef4422b3, fixer thread ordinal 6, commits d63785b7 and 2f162cdd) fixed review findings 2 to 7 and left finding 1, the air that vanishes at the bubble edge, as a fork. The user settled it on 2026-10-31 as a follow-up, ter-uu2y (the camera's chain first, then the air densest at the eye; one air per view); this ticket merges with the limit documented, because the follow-up keeps its code. Hand mutations, all killed by their tests: the pane's cover (transparent.frag), the fringe's cover (fringe.frag), a puff that takes the haze's light and a puff that the haze does not dim (puff.vert), the haze light's day factor at the eye instead of the point (atmosphere.glsl), a screen-space reflection without haze (reflection_ray.glsl), and an impostor signature without the sun's tint (ImpostorManager.cs). Coverage of the changed lines (Checked): 134/259 lines and 70/80 branches; every uncovered line but three is the demo scene DemoScenes.MarsAir.cs, which the headless script mars-air.json runs. The three were guards of PaintedAtmosphere.cs; commit ad91569c adds four tests for them (a sun that names a spot light, a sun past the snapshot's lights, an eye at the planet's center, a planet past the shaders' range), and a hand mutation of each guard fails its test.
