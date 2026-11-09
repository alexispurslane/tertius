---
id: ter-yfb5
status: in_progress
deps: []
links: []
created: 2026-11-09T04:14:05Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# A fine checker or stripe pattern shows on sunlit ground, on a moving person and on a box's face

Seen on 2026-11-08. The showcase video /private/tmp/tertius-showcase-20261108/videos/surface-apron-follow.mp4 (demo surface, follow-camera, FSR 2/3) shows a fine checker pattern on the apron ground and on the moving person, and smeared shadow edges in the shuttle (frames surface-apron-follow-*.png beside it). The raised-box shots of ter-9371, /private/tmp/tertius-diffuse-filter-20261108/box-before.png and box-after.png, show fine vertical stripes on the box's front face and a comb-like edge on its shadow, with the filter off and on. Not yet diagnosed: candidates are the 2x2 diffuse ray pattern, the shadow map's sampling, FSR's jitter, and the history.


## Notes

**2026-11-09**

Diagnosis (evidence in /private/tmp/tertius-checker-20261108/, phase_model.py). Under FSR, the sun's shadow filter took 4 of its 16 taps per pixel per draw, the tap quarter, chosen by (draw index + x + 2y) & 3. FSR's jitter is Halton base 2 over the draw index in x, so a sample lands in the right half of its pixel exactly in the draws of even index. The quarter's horizontal tap offset flipped with the same parity. So the offset that a sample carried depended on where it landed, FSR never averaged the four quarters, and a fixed comb 3 output pixels wide showed in every soft shadow. Every symptom of this ticket lies in soft shadow: the apron's "checker" is the penumbra of the shuttle's and the person's shadows, the person's stripes are on its shaded side, and the box's comb is its shadow's edge. Decision 34 of docs/plans/temporal-renderer.md assumed that FSR averages the quarters.

Fix. Every view takes all 16 taps. The tap quarter, its specialization constant (temporalShadows, constant_id 7), its tile path and the draw index in renderParams.w are gone, so one shadow filter remains.

Test. UpscalingRenderTests.ASoftSunShadowEdgeShowsNoStripeThreeOutputColumnsWideUnderFsr renders a slab's penumbra under FSR 2/3 and measures the amplitude of a 3-column period in 8-bit levels: 5.09 on main (red, for the right reason), 0.04 with the fix, limit 0.5.

Before and after (/private/tmp/tertius-shadow-taps-20261108/shots/): apron-edge.png, shuttle-bars.png and person.png show the comb gone. The person's 3-column amplitude fell from 1.47 to 0.69 levels.

Mutants (tools/mutate.py, Rendering, C# and GLSL):

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S01 | src/Tertius.Rendering/Shaders/shadows.glsl:42 | cascade filter drops its first tap row | killed | ShadowTests.AFarShadowInTheMapsStaysTheSameWhenAnotherLightsSetGainsACaster, ShadowTests.AFarShadowOfACasterThatTheSetDoesNotDrawStaysTheSameWhenTheSetDrawsAnotherCaster | 82.3 |
| S02 | src/Tertius.Rendering/Shaders/shadows.glsl:44 | cascade filter takes every second tap column | killed | ShadowTests.AFarShadowInTheMapsStaysTheSameWhenAnotherLightsSetGainsACaster, ShadowTests.AFarShadowOfACasterThatTheSetDoesNotDrawStaysTheSameWhenTheSetDrawsAnotherCaster | 69.9 |
| S03 | src/Tertius.Rendering/Shaders/shadows.glsl:52 | cascade filter weight 1/15 | killed | ShadowTests.AFarShadowInTheMapsStaysTheSameWhenAnotherLightsSetGainsACaster, ShadowTests.AFarShadowOfACasterThatTheSetDoesNotDrawStaysTheSameWhenTheSetDrawsAnotherCaster | 78.7 |
| C01 | src/Tertius.Rendering/VulkanRenderer.cs:863 | renderParams.w 0 -> 3 | killed | UpscalingRenderTests.NativeRenderingKeepsOddOutputDimensionsAndNoJitter | 51.4 |
| C02 | src/Tertius.Rendering/ShaderConstants.cs:147 | specialization storage of 9 rows | killed | ShaderConstantsTests.TheDefaultConstantsSpecializeAShaderToItsOwnDefaults (3 shaders) | 7.3 |
| C03 | src/Tertius.Rendering/ShaderConstants.cs:134 | VkBool inverted | killed | ShaderConstantsTests.TheDefaultConstantsSpecializeAShaderToItsOwnDefaults (3 shaders) | 7.6 |

6 rows: killed 6, timeout 0, survived 0, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Removed lines. The cascade quarter's behavior is the red run of the stripe test on main. The tiles' quarter (4 gathers) went with it. It is the same mechanism, and no test renders a tile's penumbra under FSR. That is a judgment call: the code no longer exists to come back. No Core, Simulation or Physics line changed, so no Stryker run.

Coverage. Changed C# lines of Rendering in Release: 12 of 12 covered.

Review (one reviewer). It found the fix correct and portable: the gap at constant_id 7 is legal (ids must be unique, not contiguous), and every bool entry is 4 bytes. Findings and what was done:
- F2-1, the other symptoms: all lie in soft shadow, shown in the before and after crops above.
- F2-2, the block ray's turn flips its pixel's column parity with the draw index: filed as t2-8foa, not measured here.
- F3-1 test summary too long: cut to the scene and a pointer to decision 34.
- F3-2 renderParams lanes undocumented: commented in frame.glsl and GpuTypes.cs.
- F3-3 LightChangeDraws comment told history: shortened, the reason now lives in ARCHITECTURE.md and t2-hvez.
- F3-4 PenumbraRows could throw without a cause: an assertion names it.
- F3-5 stale "stochastic taps" heading and row: renamed and marked.
- F3-6 the still rule omitted FSR: fixed in ARCHITECTURE.md and the StillRule.For comment, and the test passes warmUpFrames.

The commit report flagged ShaderConstants.Specialization at CC 12: its bool conversions moved into one helper. The renderer constructor's length (93 lines) is untouched beyond one removed line.

Cost (paired timing, /private/tmp/tertius-shadow-taps-20261108/timing and timing2). Demo surface, follow-camera, 240 film frames, 1920 × 1080 output at FSR 2/3 (1280 × 720 render), --in-flight 2, under the GPU lock and gpu-clock, main against the branch, the order alternating by round. Every run held 1296 MHz. Two Android emulators of another project shared the GPU, so most runs fell into a slow mode (frame span 50 to 75 ms instead of 24.5 ms) and pairs that straddle modes differ by up to 14 ms. In the 5 rounds where both runs ran in the quiet mode, the 16 taps minus the quarter were +0.03, +0.18, +0.24, +0.27 and +0.48 ms for the range G-buffer..transparent (median +0.24 ms, about 1.1% of 20.8 ms), and −0.02 to +0.40 ms for the frame span. Over all 30 rounds, the paired median of the range is +0.05 ms (first batch, 18 rounds, 90% interval −0.63 to +0.95) and +0.38 ms (second batch, 12 rounds, +0.16 to +1.32). So the 16 taps cost about 0.2 to 0.25 ms, above the brief's guess of 0.1 ms. The parent chose to keep all 16 taps and one filter, with no hashed quarter, and ARCHITECTURE.md and decision 34 record the cost.
