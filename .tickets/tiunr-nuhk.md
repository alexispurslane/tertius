---
id: tiunr-nuhk
status: closed
deps: []
links: []
created: 2026-11-08T19:49:01Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-icn3
---
# Keep the tube light's inputs in one place in lighting.glsl

Review finding Q2 of 2026-11-08. The 1 cm floor of a tube's radius is written twice in lighting.glsl (near lines 298 and 504) and once in TubeView.cs. pathLight branches on TubeKind again and recomputes toCenter and the half segment that lightIrradiance already had. lightIrradiance has two tube-only out parameters, which scatteredLight declares and drops. Put the floored radius and toCenter in LightPath, so that a change to the tube's inputs touches one place. The rendered output must stay byte-identical: compare the SPIR-V and the tube light render tests.


## Notes

**2026-11-08T21:25:30Z**

Done on branch tiunr-nuhk-tube-inputs-one-place. LightPath now holds toCenter, the half segment and the floored tube radius. lightIrradiance fills one LightPath in place of four out parameters, so scatteredLight declares no unused locals. tubeResponse reads the tube's inputs from the path and takes no GpuLight. The 1 cm floor appears once in lighting.glsl. TubeView.cs keeps the C# copy's floor. pathLight still branches on TubeKind, because the kind chooses the response. A test of the solid angle could change the output.

SPIR-V: /private/tmp/tertius-followups-20261108/nuhk/compile_all.sh compiles every engine GLSL module as the csproj does. On main its output equals the build's obj folder byte for byte. 60 of 66 modules stay byte-identical. The nine FSR modules include no engine GLSL and were not compared. spirv-diff shows the six that differ:
- direct.frag, environment_face.frag, fringe.frag, transparent.frag: one FMax and one VectorShuffle fewer per copy of the light loop. These are the second floor of the radius and the second read of the half segment from a second load of the light. Their users take the values of lightIrradiance. Every other changed line holds the same instruction with renamed operands.
- impostor_capture.frag: the same two removals, plus 16 OpPhi and 1 OpUndef that carry the two tube fields across its blocks. A non-tube light leaves them undefined and never reads them.
- puff.vert: glslc lowers `p.d == 0.0` after `||` as a short-circuit branch, not as an OpLogicalOr. The value is the same.
The removed recomputation is the goal of the ticket, so the modules cannot stay byte-identical. TubeLightRenderTests and LitPuffRenderTests pass, 46 tests.

Coverage: no GLSL coverage tool exists. The mutants below exercise each changed line.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S01 | src/Tertius.Rendering/Shaders/lighting.glsl:319 | no floor of the tube radius | killed | TubeLightRenderTests.ATubeLightLightsAMatteSquareAsTheCSharpTubeDoes(name: "hairline") | 28.7 |
| S02 | src/Tertius.Rendering/Shaders/lighting.glsl:300 | no zero irradiance before the early returns | survived |  | 36.2 |
| S03 | src/Tertius.Rendering/Shaders/lighting.glsl:318 | path half segment doubled | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize, TubeLightRenderTests.ATubeLightLightsAMatteSquareAsTheCSharpTubeDoes(name: "slanted-through-the-square") | 29.6 |
| S04 | src/Tertius.Rendering/Shaders/lighting.glsl:508 | specular tube radius doubled | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize | 29.6 |
| S05 | src/Tertius.Rendering/Shaders/lighting.glsl:508 | specular toCenter -> L | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize, TubeLightRenderTests.ATubeLightLightsAMatteSquareAsTheCSharpTubeDoes(name: "slanted-through-the-square") | 30.1 |
| S06 | src/Tertius.Rendering/Shaders/lighting.glsl:645 | puff irradiance -> cosine | killed | LitPuffRenderTests.ALitPuffInsideASpotLightsConeIsLitByItAndOneOutsideTheConeIsNot, LitPuffRenderTests.ALitPuffScattersTheSunlightAtItsCenterOverFourPi, LitPuffRenderTests.ALitPuffScattersAPointLightByItsDistanceAttenuation | 34.7 |
| S07 | src/Tertius.Rendering/Shaders/lighting.glsl:643 | puff at the light center not skipped | killed | LitPuffRenderTests.ALitPuffAtALightsExactCenterTakesNoLightFromIt | 34.5 |
7 rows: killed 6, timeout 0, survived 1, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

S02, judgment call, not equivalent: its SPIR-V compares an OpUndef irradiance on the early-return paths, which MoltenVK happens to read as a passing value, so no test on this driver can pin it.
