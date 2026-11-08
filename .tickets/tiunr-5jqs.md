---
id: tiunr-5jqs
status: closed
deps: []
links: [tiunr-5qf3, ter-12xp, tiunr-1lss]
created: 2026-11-08T16:22:16Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-icn3
---
# A tube light lets a glowing bar light the scene without specks

User decision 2026-11-08. The floor's white specks come from emissive objects that hold no light: only rare random rays carry their light (tiunr-5qf3 notes). The engine already sends an entity's power once, through its light (frame.glsl glowSent, user rule 2026-10-27), and the demo's round GlowLamp follows it with a sphere light. A bar needs a tube light: a capsule (segment plus radius) in the exact light loop, GGX specular by the representative point with energy normalization, a documented diffuse approximation checked against a numerical reference, and a shadow from an approximate point. Then the demo's glowing bar and the capture tool's plate emitter hold tube lights. Show the user only four before/after videos (moving camera and moving emitter, smooth and rough).


## Notes

**2026-11-08T18:18:32Z**

Hardening of the tube light (2026-11-08).

Mutation testing: tools/mutate.py in a scratch git copy (now deleted), 54 listed mutants. Core and Simulation lines also went to mutate.py, not to the Stryker run that tools/README.md names; that is a deviation.

| ID | Place | Change | Result | Killed by, or reason |
|---|---|---|---|---|
| C01 | LightData.cs:72 | capsule area without its ends | killed | TubeLightDataTests.ATubeOfLengthZeroSendsTheRadianceOfTheSphereOfItsRadius, TubeLightDataTests.ACapsuleSendsIt... |
| C02 | RenderSnapshotBuilder.cs:65 | tube axis -Z | killed | RenderSnapshotLightingTests.ATubeLightFollowsItsEntityAndTurnsItsSegmentWithIt |
| C03 | RenderSnapshotBuilder.cs:689 | previous axis from current pose | killed | RenderSnapshotLightingTests.ATubeLightFollowsItsEntityAndTurnsItsSegmentWithIt |
| C04 | RenderSnapshotBuilder.cs:693 | length from radius | build-failed | The analyzers reject the mutant (IDE0059, an unused value), so it cannot build. |
| C05 | LightUnits.cs:49 | zero-size capsule takes the formula | killed | TubeLightTests.ACapsuleOfNoSizeHasThePeakIntensityOfAPointLightOfItsPower |
| C06 | LightUnits.cs:50 | peak intensity side width R | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize, TubeLightRenderTests.ATubeLightLightsAM... |
| C07 | SceneBatcher.cs:577 | tube holds no entity | killed | BatcherTests.OnlyTheGpuInstancesOfATubeLightsEntityCarryItsLightHolderMark |
| C08 | SceneBatcher.cs:1110 | tube prepared as point | build-failed | The analyzers reject the mutant (IDE0051, HalfSegmentOf unused), so it cannot build. |
| C09 | SceneBatcher.cs:1118 | reach without half length | build-failed | The analyzers reject the mutant (IDE0059, an unused value), so it cannot build. |
| C10 | SceneBatcher.cs:1177 | zero axis along X | killed | BatcherLightTests.ATubeLightWithoutAnAxisStandsUpright |
| C11 | SceneBatcher.cs:1178 | previous axis ignored | killed | BatcherLightTests.ATubeLightReachesTheShadersAsItsPeakIntensityItsHalfSegmentAndAReachThatHoldsItsSegment |
| C12 | SceneBatcher.cs:1179 | half segment full length | killed | BatcherLightTests.ATubeLightReachesTheShadersAsItsPeakIntensityItsHalfSegmentAndAReachThatHoldsItsSegment, Bat... |
| C13 | SceneBatcher.cs:1231 | axis finiteness unchecked | killed | BatcherLightTests.ATubeLightWithABrokenFieldDoesNotShine |
| C14 | SceneBatcher.cs:1232 | previous axis finiteness unchecked | killed | BatcherLightTests.ATubeLightWithABrokenFieldDoesNotShine |
| C15 | SceneBatcher.cs:1234 | negative length shines | killed | BatcherLightTests.ATubeLightWithABrokenFieldDoesNotShine |
| C16 | SceneBatcher.cs:1234 | endless length shines | killed | BatcherLightTests.ATubeLightWithABrokenFieldDoesNotShine (new test, rerun) |
| C17 | GpuTypes.cs:342 | zero half segment is a tube | killed | BatcherLightTests.ATubeLightOfLengthZeroReachesTheShadersAsThePointLightOfItsPower, BatcherLightTests.AKeptLig... |
| C18 | GgxLobe.cs:89 | along width = cross width | build-failed | The analyzers reject the mutant (IDE0060, alongSine unused), so it cannot build. |
| C19 | GgxLobe.cs:77 | sphere normalization not squared | killed | SphereLightLobeTests.AMirrorShowsALargeLightAtNearlyItsOwnSurfaceRadianceInsideItsDisc, SphereLightLobeTests.A... |
| C20 | MaterialSample.cs:84 | zero cosine shades | survived | Judgment call: it differs only where n · cosine vector is exactly 0.0, where the diffuse part is 0 either way; no input reaches that float exactly. |
| C21 | MaterialSample.cs:92 | specular at n.l = 0 | survived | Judgment call: at an exact n · l of 0 the specular part is multiplied by that 0; no input reaches it exactly. |
| C22 | MaterialSample.cs:100 | specular without solid angle | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize, TubeLightRenderTests.ATubeLightLightsAM... |
| C23 | MaterialSample.cs:103 | diffuse without 1/pi | killed | TubeLightRenderTests.ATubeLightLightsAMatteSquareBelowItsMiddleAsTheCSharpTubeDoes, TubeLightRenderTests.ATube... |
| C24 | TubeView.cs:19 | no radius floor | killed | TubeLightTests.ATubeThinnerThanOneCentimeterCountsAsOneCentimeterThick, TubeLightRenderTests.ATubeLightLightsA... (new test, rerun) |
| C25 | TubeView.cs:28 | side floor also beyond the ends | killed | TubeLightTests.OnItsAxisATubeSendsOnlyTheLightOfItsEnds |
| C26 | TubeView.cs:30 | near end = far end | killed | TubeLightTests.ATubeLightLightsAFloorPointAsTheIntegralOverItsCapsuleDoesWithinTwoAndAHalfPercent, TubeLightTe... |
| C27 | TubeView.cs:35 | angle formula sign | killed | TubeLightTests.ATubeLightLightsAFloorPointAsTheIntegralOverItsCapsuleDoesWithinTwoAndAHalfPercent, TubeLightTe... |
| C28 | TubeView.cs:52 | ends are full spheres | killed | TubeLightTests.OnItsAxisATubeSendsOnlyTheLightOfItsEnds, TubeLightTests.ATubeLightLightsAFloorPointAsTheIntegr... |
| C29 | TubeView.cs:100 | parallel ray takes the start | killed | TubeLightTests.AMirrorRayAlmostAlongATubesSegmentTakesItsMiddleAsTheRepresentativePoint (new test, rerun) |
| C30 | TubeView.cs:106 | along width = cross width | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize, TubeLightTests.AMirrorShowsTheInsideOfA... |
| C31 | TubeView.cs:94 | parallel threshold 0 | killed | TubeLightTests.AMirrorRayAlmostAlongATubesSegmentTakesItsMiddleAsTheRepresentativePoint (new test, rerun) |
| S01 | lighting.glsl:206 | side floor also beyond the ends | killed | TubeLightRenderTests.ATubeLightLightsAMatteSquareAsTheCSharpTubeDoes (new test, rerun) |
| S02 | lighting.glsl:208 | near end = far end | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize, TubeLightRenderTests.ATubeLightLightsAM... |
| S03 | lighting.glsl:223 | side cosine angle term | killed | TubeLightRenderTests.ATubeLightLightsAMatteSquareAsTheCSharpTubeDoes, TubeLightRenderTests.ATubeLightLightsAMa... (new test, rerun) |
| S04 | lighting.glsl:225 | ends are full spheres | killed | TubeLightRenderTests.ATubeLightLightsAMatteSquareBelowItsMiddleAsTheCSharpTubeDoes, TubeLightRenderTests.ATube... |
| S05 | lighting.glsl:255 | along width = cross width | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize |
| S06 | lighting.glsl:252 | parallel ray takes the start | survived | Judgment call: the GPU copy of C29. It needs a mirror ray within 0.06 degrees of the segment at a pixel center. The C# test locks the choice. |
| S07 | lighting.glsl:283 | tube branch for spots | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize, TubeLightRenderTests.ATubeLightLightsAM... |
| S08 | lighting.glsl:289 | window from the middle | survived | Judgment call: the window and the cut by distance differ only where the light is within about one 8-bit step of black (2^-13 pre-exposed radiance). A test of the shown image cannot resolve that within 2 %. BatcherLightTests covers the reach. |
| S09 | lighting.glsl:292 | segment reach = reach | survived | Judgment call: as S08, the fade near black only. |
| S10 | lighting.glsl:297 | no radius floor | killed | TubeLightRenderTests.ATubeLightLightsAMatteSquareAsTheCSharpTubeDoes (new test, rerun) |
| S11 | lighting.glsl:300 | diffuse toward the middle | killed | TubeLightRenderTests.ATubeLightLightsAMatteSquareBelowItsMiddleAsTheCSharpTubeDoes, TubeLightRenderTests.ATube... |
| S12 | lighting.glsl:303 | face radiance area | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize, TubeLightRenderTests.ATubeLightLightsAM... |
| S13 | lighting.glsl:463 | takesLight toward the middle | killed | TubeLightRenderTests.ATubeLightLightsAMatteSquareAsTheCSharpTubeDoes (new test, rerun) |
| S14 | lighting.glsl:466 | surface shadow normal unturned | survived | Judgment call: the turned normal only moves the shadow lookup offset. In the one case that reaches it (slanted-through-the-square, with shadows) the image shows no difference at 2 %. |
| S15 | lighting.glsl:488 | fiber diffuse share | survived | Judgment call: hair under a tube light. No scene puts hair under one (the hair-rugs panel holds no light), and a test needs a fiber GPU fixture. |
| S16 | lighting.glsl:497 | zero cosine shades | survived | Judgment call: the GPU copy of C20, the same exact-zero case. |
| S17 | lighting.glsl:499 | diffuse without 1/pi | killed | TubeLightRenderTests.ATubeLightLightsAMatteSquareBelowItsMiddleAsTheCSharpTubeDoes, TubeLightRenderTests.ATube... |
| S18 | lighting.glsl:514 | toCenter unit length | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize |
| S19 | lighting.glsl:140 | along width = cross width | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize |
| S20 | brdf.glsl:87 | along width not spread | killed | TubeLightRenderTests.AMirrorReflectsATubeLightAsABarOfTheCapsulesSize |
| C32 | TubeView.cs:28 | no inside floor beside the segment | killed | TubeLightTests.APointInsideATubesCapsuleBesideItsSegmentTakesTheLightOfItsSurface (new test, rerun) |
| C33 | TubeView.cs:27 | always the beside form | killed | TubeLightTests.OnItsAxisATubeSendsOnlyTheLightOfItsEnds |
| S21 | lighting.glsl:206 | no inside floor beside the segment | survived | Judgment call: the GPU copy of C32. A shaded point inside a capsule exists only where geometry crosses the light. The C# test locks the floor. |

Summary: 54 mutants, 41 killed, 4 rejected by the compiler, 9 survived with a judgment call, 0 without an answer.

Coverage of the changed C# lines (Debug, Core, Simulation and Rendering): 145 of 146 lines, 23 of 26 branches before the new tests. The gaps (MaterialSample.cs:85, LightUnits.cs:49, TubeView.cs:27) now have tests: a surface facing away, a capsule of no size, a point on the axis past an end.

Suites, final code, in the worktree: Checked 10,394 total, 10,370 passed, 0 failed, 24 skipped. Release 10,381 total, 10,357 passed, 0 failed, 24 skipped. No snapshot failed, so no reference image was re-approved.

Approximations:
- Specular: Karis 2013 representative point (the segment point nearest the mirror ray, then the sphere step), normalized by an ellipse of the cross and along half-widths.
- Diffuse: the side as a line of elements of width 2R sin psi (closed-form vector irradiance), and the ends as half spheres of pi R^2 / 2. Against a numerical integral at nine floor points: 0.01 % to 2.0 % (tolerance 2.5 %); solid angle within 0.7 %. On the axis past an end only the ends light; close to the axis there, the result is 1/2 (1 + d_near^2 / d_far^2) of the truth (0.53 for the demo bar seen 0.5 m above its top).
- Shadow: from the segment middle, through the point-light shadow paths.
- Thin tubes: R floored at 1 cm, peak intensity kept.

Found and fixed during hardening: the side floor at R also acted past the ends, which added side light on the axis (+24 % end-on). The floor now applies only beside the segment.

Wide glow on the capture plate: about 40 % of the glow before the tube light. A GPU test of a standing tube against the C# integral passes, so the new level is taken as right. The cause of the old, higher level is not proven.

Not measured: the GPU cost of a tube light.

**2026-11-08T18:19:55Z**

2026-11-08: verified: final-checked 10,394 run 0 failed, final-release 10,381 run 0 failed; looked at the video check frames and the demo front view (no specks, the box and ball clean). Open: the old ray-lit wide glow was about 2.5 times the new direct level (tiunr-1lss).
