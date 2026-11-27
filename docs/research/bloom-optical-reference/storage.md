# Source-radiance storage audit

This audit records the source representation for [#3](https://github.com/alexispurslane/tertius/issues/3) and its shared contract with analytic exposure in #4. The initial proposal is retained below as design history. The settled source model supersedes it. The measured 128/256 candidate and its inspected scene controls are recorded in [candidate-2026-11-27.md](candidate-2026-11-27.md). The verified common source commit is adopted. Final integration checks, independent review and Linux cost measurements remain pending.

## Ownership

The scene HDR image holds the current shaded scene. The exposure meter measures it. FSR derives a reconstructed base image from it. Bloom must derive its source response from the original scene image because FSR's inverse tone map limits large reconstructed values.

Emission and direct-light images are intermediate shading results. They are not competing copies of the final scene. Existing indirect estimators have bounded samples and histories. Keeping their numerical budgets does not recover indirect light already lost inside those estimators.

## Initial proposal, superseded by the settled scene-referred model

All bounds below are per color channel. Pre-exposed radiance means radiance in W/(m² sr) multiplied by the exposure scale. A demodulated value is pre-exposed radiance divided by its material factor.

| Stage | Current representation and limit | Proposed treatment |
|---|---|---|
| CPU light and material inputs | Finite radiance through 2^64, irradiance through 2^64, exposure scale 2^-64 through 2^64 | Keep these input ranges. |
| G-buffer emission | RGBA16F, pre-exposed emission capped at 32,256 | RGBA32F with a finite scene bound. The lamp probe first clips here. |
| Direct diffuse and specular | RGBA16F, demodulated and capped at 32,256 | RGBA32F with the scene bound. Cap before division by a small material factor. The panel probes first clip here. |
| Composite scene HDR | RGBA16F, surface sum capped at 32,256 | RGBA32F. Restore material factors and add emission before the scene bound. |
| Skin diffuse and row intermediate | RGBA16F | RGBA32F so the scatter does not clip a widened direct diffuse source. The scatter changes only skin pixels. |
| Transparent glass and cutout fringe | Blend into RGBA16F HDR. Each source is bounded by 32,256 times its blocked fraction | Blend into RGBA32F HDR with the corresponding scene bound times blocked fraction. Keep the same transmittance model. |
| Puffs | Their aggregate exact contribution is at most 16,128. Rounded blends may add at most twice that | Give puffs half the scene bound as their aggregate budget. Keep the existing division by puff count. |
| Haze | Convex mixture with scene HDR | Keep the convex mixture with finite scene-bound inputs. |
| Impostor color atlas | RGBA16F, captured radiance capped at 32,256, then pre-exposed by the billboard | RGBA32F with the scene bound at capture and at billboard exposure. Otherwise distant emissive sources would still lose radiance. |
| Environment surface records | RGBA32F records of geometry and material | No representation change. |
| Environment radiance, diffuse and specular maps | RGBA16F. Face lighting caps unexposed radiance at 32,256 | Retain this existing indirect approximation and document its limit. It cannot supply uncapped indirect radiance. |
| Screen-trace samples and histories | Mostly RGBA16F, capped at 32,256. An existing optional slow-specular mode uses RGBA32F | Retain their existing budgets, history rounding and exposure rescaling. A source-path fix does not claim recovery of a clipped indirect estimate. |
| Light volume | Existing directional transport and its own storage/normalization | Keep its existing estimator. Its result joins the scene before the scene bound. |
| FSR opaque input copy | Same format as scene HDR | Keep a copy of the widened input in the format required by FSR's current input path. This is already an existing derived input. |
| FSR internal histories | RGBA16F and AMD's existing tone/inverse-tone functions | Keep AMD's implementation. Its inverse tone map uses a denominator floor of 2^-14. |
| FSR reconstructed output | Current output-size HDR image | Keep its existing role as base reconstruction. Do not use it as the authoritative bloom source. |
| Bloom prefilter | Reads reconstructed HDR; thresholds individual texels before filtering | Read original scene HDR with active-size and jitter mapping. Apply the normal threshold and artistic extreme response before filtering. |
| Bloom levels | RGBA16F, divided by level count before additive upsampling | Retain RGBA16F only with a separate proven bound on the artistic response. The response has not been selected yet. |
| Tone mapping | Single-precision arithmetic over base plus bloom | Keep the same tone curves and strength. Include intermediate products in the finite-value proof. |

The direct/emission path is the observed defect. The retained indirect-estimator limits are an explicit scope boundary. A claim that *all indirect transport* preserves every original source value would require a separate change to those estimators and their formats.

## Rejected pre-exposed bound

Let M = 2^48. This is a proposed numerical safety bound, not the artistic knee. Let k be the exposure scale, between 2^-64 and 2^64. Let d be a material divisor, between 2^-14 and 1. The existing material-factor analysis in ARCHITECTURE.md gives the upper bound 1.

- M/k lies between 2^-16 and 2^112. Both are normal, finite single-precision values.
- Cap radiance at (M/k)·d **before** division by d. The quotient is at most M/k, and multiplication by k is at most M, apart from rounding. This avoids overflowing a demodulated intermediate before a later clamp.
- The existing direct-light loop bounds each light's contribution below about 10^27 and its supported sum within a finite single float. That bound must remain true. The new cap cannot repair an infinity that the loop has already produced.
- Restoring a material factor no greater than 1 gives a direct term at most M. Adding two direct terms, two bounded indirect terms and emission stays below 5M, or below 2^51, before the final scene cap.
- The skin scatter is a sum with normalized positive channel weights. Its result stays within its source range. A conservative bound of 4M covers scatter addition/subtraction and rounding before the final clamp. The kernel normalization is already a separate engine invariant.
- A transparent source bounded by M(1-T), with transmittance T in [0,1], preserves an M-bounded surface image in exact arithmetic. The existing puff-budget argument supplies at most another M after rounded puff blends. A conservative scene bound is therefore 4M = 2^50 after rounding and subsequent convex haze/glass operations.
- The tone curves square their inputs. The ACES matrices and Hable exposure bias introduce small fixed factors. Even a conservative input bound of 2^54 gives squared products below 2^108, below the single-float overflow exponent of about 128. Finite output storage alone would not establish this.
- An impostor divides premultiplied color by coverage only after the existing coverage floor. That division needs its own pre-division bound. It must not rely on a later scene cap to repair overflow.

These are design bounds. Implementation must preserve their order of operations. Tests must exercise the largest inputs and both exposure endpoints. Half-float indirect stores still require their original 32,256 limits. Half-float bloom stores need the final extreme-response proof, including upsampling and rounding.

## Format evidence

RGBA32F sampling, storage and color attachment use are mandatory in Vulkan's [32-bit component table](https://github.com/KhronosGroup/Vulkan-Docs/blob/main/chapters/formats.adoc#formats-mandatory-features-32bit). Linear filtering and blending are optional. Startup checks must query those features for the exact uses above.

| Driver evidence | Observed support |
|---|---|
| Linux NVIDIA RTX 2000 Ada, Vulkan 1.4.329, Runpod job `b3ecd0eb-3463-47b0-8fb2-f3ce26afc94a-u1` | Optimal tiling explicitly reports sampled image, storage image, color attachment, color-attachment blending, linear filtering, and transfer source/destination. Saved report: `issue-3-linux-formats/out/formats.txt`, lines 2387–2419, under the recovery evidence. The job took five seconds and counted $0.012. |
| Apple M1 Max, MoltenVK 1.4.2, read-only `vulkaninfo --show-formats` query | The same optimal-tiling feature bits are present. Saved report: `issue-3-mac-formats.txt`, lines 1654–1683. This query did no rendering and is not a performance measurement. |
| Mesa ANV source | [Intel's format table](https://github.com/chaotic-cx/mesa-mirror/blob/main/src/intel/isl/isl_format.c) lists RGBA32F sampling, filtering, render-target and alpha-blend support. Its filtering threshold predates supported Vulkan Intel hardware. |
| Mesa RADV source | [Format predicates](https://github.com/chaotic-cx/mesa-mirror/blob/main/src/amd/vulkan/radv_formats.c) allow blending for floating color-buffer formats; their exclusions concern integer and depth/stencil encodings. |
| Mesa lavapipe source | [Vulkan feature mapping](https://github.com/chaotic-cx/mesa-mirror/blob/main/src/gallium/frontends/lavapipe/lvp_formats.c) enables linear filtering and blending for supported non-integer formats. [llvmpipe](https://github.com/chaotic-cx/mesa-mirror/blob/main/src/gallium/drivers/llvmpipe/lp_screen.c) supports plain RGBA32F rendering and storage. |

The [Vulkan blending rules](https://github.com/KhronosGroup/Vulkan-Docs/blob/main/chapters/framebuffer.adoc) require basic blending precision and dynamic range no lower than the destination components. That supports the finite RGBA32F bound. It does not authorize an overflowing RGBA16F blend.

## Initial mapping and verification plan

The source uses active render coordinates, while bloom level sizes use output coordinates. A prefilter tap must map output coordinates into the active source and subtract the current render jitter. It must clamp to the active rectangle, including odd dimensions. The source texture's allocated size can be larger than its active rectangle.

The native unjittered path can retain the current 36 weighted texels. The mapped path can evaluate the same 13 bilinear taps, thresholding each of their four source texels before interpolation. That costs at most 52 fetches rather than 36. The constant cost requires matched Linux measurements.

Prepared motion tests cover a four-output-pixel source crossing quarter-pixel positions under no upscaling, native-size FSR and half-size FSR. They have not run yet. They measure halo energy outside the source after history warm-up. They do not establish subpixel-area accuracy for arbitrary unresolved geometry.

Actual-scene source quantiles from the first opt-in fixture are provisional: validation failed with the deformation/impostor hazard tracked by [#28](https://github.com/alexispurslane/tertius/issues/28). Do not fit the artistic response from them until a clean replay exists. Do not interpret a stored value at an existing cap as an uncensored source measurement.

## Recovered focused baseline, 2026-11-27

The exact #28 synchronization patch was adopted as prerequisite commit `3b45a9ade`.
Its original commit is `8f6e1c56306bac54c53ed3a707d2c8b058f68ef1`.
Both have stable patch identity `15d8b5cb32f81b799ba11b0001fc589e59bbdaf4`.
The dirty diagnostics, tests and research were preserved.

The focused Checked run selected `Tertius.Rendering.Tests.BloomSourceLightTests*`.
It ran nine cases: four passed and five failed for the intended behavior.
All three tenfold source controls and unjittered motion passed.
At a thousandfold input increase, the scene-source ratios were 504 for emission,
506.65027 for the matte panel and 35.137257 for the glossy panel, against 1000 ± 30.
The four-output-pixel moving source's halo range divided by mean was 0.1404099124
at native-size FSR and 0.1986702174 at half-size FSR, against a limit of 0.05.
Disposal reported no Vulkan validation failure. A preceding build attempt failed
with MSBuild worker exits before any tests. It supplies no behavior evidence.

The #28 owner ran both exact archived scene fixtures without edits, using shared
content. Raw logs are `/tmp/tertius-28-evidence/helmet.log` and `egress.log`.
Each passed one test. Raw JSON lives in the sibling `scene` directory.
Both JSON files report empty validation messages.

| Scene | Tick | Output pixels | Active render pixels | Applied exposure, stops | Original scene maximum | Reconstructed maximum |
|---|---:|---|---|---:|---:|---:|
| Helmet | 94901 | 1280 × 720 | 848 × 477 | 6.0043726 | 37.3125 | 31.515625 |
| Settled exterior | 177902 | 960 × 540 | 640 × 360 | 4.174772 | 13880 | 10256 |

These are the unchanged baseline, not validation of a new representation or
response. The settled exterior is not the adapting exposure of the continuous
800-frame egress capture.

## New analytic-exposure contract under discussion

The proposed pre-exposed bound of 2^48 above is a hard numerical cap. It cannot
preserve every supported source for analytic metering: radiance 2^64 clips even
at zero producing stops, and radiance 2^20 clips at producing exposure +64.
Subtracting the producing exposure after clipping cannot restore the source.
No production representation change was made before raising this conflict with
#4 and the line coordinator. A scene-referred source with exposure applied
once after metering would remove that dependency. Its arithmetic, indirect
history conversions and downstream bounds require a shared decision first.

## Settled source model after analytic-exposure steering

The shared model now stores scene-referred radiance in the original HDR image,
the emission image, both direct-light images, the skin images and the impostor
color atlas. A direct-light image holds actual diffuse or specular radiance,
without division by a material factor. Exposure never clips these sources.
The old proposed 2^48 source cap above is rejected. The 2^48 bound applies only
at a downstream display consumer, after the meter has read the original source.

Each light contributes less than about 2^90 at the existing irradiance and BRDF
bounds. A light record occupies 128 bytes. Even a storage buffer at the uint32
byte limit holds fewer than 2^25 records, so its nonnegative sum stays below
2^115. Two direct terms plus emission and bounded indirect terms fit below
2^118, with room for rounding. Dividing the worst direct sum by a material
factor as small as 2^-14 could exceed the float range. Storing actual radiance
removes that division. This proof uses the existing per-light arithmetic bound;
it does not authorize new unbounded products inside the light loop.

Indirect histories retain their existing bounded, pre-exposed approximation.
The composite divides their material-weighted output by the producing exposure
scale before adding it to raw direct radiance and emission. At the lowest
exposure, 32256 / 2^-64 remains below 2^79. The direct terms never pass through
that history cap. The scene tracer explicitly converts fresh raw sources into
its bounded pre-exposed sample units at its own boundary.

Downstream exposure uses min(L, 2^48 / k) * k with k = 2^stops and stops in
[-64,64]. Its cap operand stays between 2^-16 and 2^112. The product is finite,
and the tone curve's squared expressions stay below the float overflow range.
FSR's half-float derived input needs its narrower existing storage bound.
Neither derived result replaces the raw scene image.

Ownership: #3 owns source writers, source formats, the composite, skin scatter,
transparent sources, impostors, shared exposure helpers and bloom. #4 owns the
meter, adaptation, exposure resources, FSR handoff, tone mapping and conversions
at the screen tracer's history boundary. A mixed-units intermediate tree is
not a shippable change. The first published step must contain both boundaries.

## Earlier 8192/16384 candidate, superseded by the measured 128/256 response

The candidate implementation keeps excess pre-exposed brightness unchanged through
8192. Above 8192, with x = excess - 8192, the response is
8192 + 8192 x / (8192 + x). It joins with derivative 1 and approaches 16384.
Its mathematical derivative remains positive for every finite supported input.
Floating-point storage can merge nearby responses, especially near the asymptote. The ratio of
response to excess decreases above the join. Color keeps its channel ratios.
This response is artistic. The Spencer reference does not supply its join or
asymptote. The baseline helmet maximum, 37.3125 before reconstruction under its
fixed exposure, lies far below the join. This does not establish visual
acceptance: the new source uses the original scene before FSR, whose spatial
profile differs from the reconstructed source. The continuous adapting egress,
settled same-tick controls and helmet images must decide acceptance.

The prefilter safely bounds exposed source brightness at 2^48, after metering.
The rational response is therefore finite, and never exceeds 16384 per channel.
Each positive filter has weights summing to one. Each level begins with the
response divided by the number of levels. At most fourteen levels add into
level zero. Allowing even a factor of two for storage and blend rounding keeps
that sum below 32768, far below the half-float overflow point of 65504. This
bound is independent of source area, bloom strength and output resolution.
Strength belongs to the tone consumer and needs its own safe-product bound.

The mapped prefilter evaluates the same thirteen bilinear taps as the native
filter. It maps each tap from output pixels to active source pixels, subtracts
the producing draw's jitter, then thresholds each of its four source texels
before interpolation. Each fetch clamps to the active source rectangle.
The allocated source extent never defines this boundary. At native size with
zero jitter, the existing thirty-six weighted-texel path remains.

Additional first-red evidence is in `/tmp/issue3-contract-red.log`: the extreme
lamp's halo ratio was exactly 1 for a tenfold increase from 64000 to 640000,
against the required range 1.01 through 2. The original HDR failed to retain
radiance 64 at both -64 and +64 exposure stops. Three ordinary source controls
passed. This run selected the outer class only, not the nested motion class.
The earlier attempt failed the named-argument analyzer before testing and is
not red behavior evidence.

The source patch was transferred by named file to the #4 worktree for a coherent
source/consumer commit. Neither worktree publishes a mixed-units intermediate.
The patch includes all forward-surface call sites of the simplified radiance
functions. The scene source does not contain a second exposure authority.

## Source arithmetic audit for the shared model

| Operation | Inputs and intermediate bound | Stored units |
|---|---|---|
| Emission upload and G-buffer write | CPU material boundary limits each channel to 2^64. No exposure product occurs. | Scene radiance |
| Direct light loop | Per-light contribution below about 2^90; fewer than 2^25 records in a uint32-sized buffer; sum below 2^115. No demodulation or exposure product follows. | Scene diffuse/specular radiance |
| Composite | Two direct terms below 2^115, emission below 2^64, bounded history terms converted below 2^79. The sum stays below 2^118 with rounding headroom. | Scene radiance |
| Skin scatter | Positive normalized channel weights bound each convolution by its source maximum. The update lit + scattered - diffuse stays below 2^119 before the lower clamp. | Scene radiance |
| Impostor capture and mip filter | Same finite forward-surface sum; positive filtering uses finite normalized weights. Billboard coverage is at least 0.5, so unpremultiplication adds at most a factor of two. | Scene radiance |
| Glass | Forward radiance is computed before the guard, using the finite light-loop bound. The guard 2^120 times blocked fraction preserves a convex blend bound independent of exposure. | Scene radiance |
| Fringe and haze | Positive coverage/transmittance mixtures of finite scene inputs. Coverage and transmittance lie in [0,1]. | Scene radiance |
| Puffs | Existing aggregate source budget 16128 is retained in scene units. It is a separate approximation and does not cap direct surface sources. | Scene radiance |
| Source sampling for bloom | Texel reads precede exposure. Cap the source operand at 2^48 / 2^stops before multiplying. | Derived pre-exposed radiance |

The 2^120 glass guard exceeds the supported opaque-source bound. It is not the
artistic glare join. A conservative factor of two for rounded transparent
blends still lies below 2^121. Subsequent source interpolation and normalized
filters remain well inside single precision. There is no half-float surface
blend and no dependence on Metal's half-float saturation.

The forward glass guard retains the previous blocked-fraction rule. A fully
transmitting pane contributes no surface term under that rule. The retained
puff source budget and bounded environment/screen estimates are explicit
approximation limits. This change does not claim uncensored indirect transport
or a new material/transmittance model.

Format guards query the exact RGBA32F operations: color attachment, blending,
sampling, linear filtering, storage and transfer source/destination. The
impostor atlas additionally retains its existing blit/filter checks. Bloom
levels remain RGBA16F and use their separate response bound. Source format and
bloom format have separate names so one can no longer silently select the
other's attachment format.

## First integration findings

The default key-and-fill direct-light shader was a second source writer. The
first source patch changed the exact-every-light shader but missed that writer.
The integrated source tests correctly retained their failures: matte source
ratio 506.676 and glossy source ratio 35.10619. The high-radiance reflection
probes also remained at 32256 or 64512. The key-and-fill shader now writes the
same actual scene-referred diffuse/specular radiance as the exact shader.
These are production findings; their assertions were not relaxed.

The endpoint test's first failure was a test-helper error, not valid red
behavior: its helper always reads bloom level zero, but that test disabled
bloom. It now enables bloom, which does not alter the original scene image.
A scripted writer mutant must demonstrate this endpoint invariant after green.
The nonfinite-puff test also retained an obsolete Half cast in its expected
value. Its comparison now uses 0.2 within 1e-6 for the single-precision scene
image, while keeping the full-image finite-value assertion.

The combined run also exposed unused half-float history bindings that still
pointed at widened direct images. The companion fixes those descriptor formats
and declares their image use once, preserving the real preceding dependency.
Ordinary exposure adaptation must run after both bloom and tone mapping:
it writes the next frame's exposure, while those consumers read the current
frame's exposure. The companion owns this ordering fix and the corresponding
buffer barriers. No implementation scene capture ran before these findings.

## Measured candidate after recovery

The source suite passed 19 cases, including both direct-light paths and the moving highlight under no upscaling, native-size FSR and half-size FSR. The separate source-report run passed 16 cases and retained per-path reports. The normal lamp energy and three ring means exactly match the saved baseline. The original thousandfold source assertions are unchanged.

The current artistic response is linear through excess brightness 128 and approaches 256. The earlier 8192/16384 and 1024/2048 candidates produced an unacceptable white veil over the hull. This tuning acts downstream of the original scene and meter. It does not change the source storage proof.

The clean 800-frame candidate and the bloom-off control apply identical float32 exposure bits on every frame. The fixed-exposure helmet capture and both same-tick settled exterior controls also passed with no validation messages. The coordinator independently inspected continuous frames 500 and 600, the helmet and the settled pair. Those inspected images preserve recognizable hull structure and normal helmet glow. This is scoped visual evidence, not approval of every frame.

The 22-case bloom class passed after the extreme-square probe's strength changed from 1/65536 to 1/256. The old probe showed zero at the tested halo pixel after the new bounded artistic response. Its visible-halo interval [0.05,0.95] is unchanged, and its finite level bound tightened from 32768 to 512. A separate static fix skips subnormal knee widths before division. Their soft contribution cannot survive the half-float store, which occurs before Strength multiplication, so even float.MaxValue Strength cannot recover it.

Ten scripted bloom mutants completed through tools/mutate.py with its staged-input checks. Nine were killed after a scoped regression-test rerun; the subnormal guard survivor has an explicit portability judgment. The detailed table is in [candidate-2026-11-27.md](candidate-2026-11-27.md). The common commit includes the near-field rectangle correction and consumer unit conversions. Its full Rendering run passed 3,645 cases with 12 skipped and none failed. The final source arithmetic evidence below includes that correction.


## Verified common source adopted on 2026-11-27

The source/consumer prerequisite is commit `4da7acd35eada358715ce22ee1e914264ac393c7`. Its normal commit hook passed. The five-file cleanup in `9d6c5295a0952e896ac7165ea2bcfd2ec0a2b3a5` changes formatting and type inference only. This branch retains the common shader and consumer bytes, with the separate measured 128/256 response in the bloom prefilter. The common commit's 8192/16384 response is an earlier candidate.

The adopted rectangle light uses the existing angular integral for near diffuse light. A point approximation applies only at distances of at least eight half diagonals, with the face above the receiving horizon. The projected far solid angle is at most 4ab / [64(a²+b²)] ≤ 1/32 sr, where a and b are the half-side lengths in metres. The exact near integral bounds diffuse reflection by the face radiance times the diffuse color factor. This restores the physical premise of the conservative light-loop bound above.

The rectangle's center and both half-side vectors share the reciprocal of max(center distance, largest half-side length, 1e-6 m). Every normalized vector has length at most one. Corner components have magnitude at most three, so squared corner lengths are at most 27. This common scale preserves the angular integral. Separately normalized axes define the face direction, and separately computed half-side lengths define its area. A large cross product is never squared to obtain that area. The far predicate divides distance by eight before squaring. The supported input bounds remain `ShaderInput.PositionLimit = 2^62` m and rectangle half-side length at most `2^61` m.

The common `exposed(value, scale, cap)` helper keeps each finite nonnegative multiplication finite. For scale at most one, multiplication precedes the cap. For larger scale, division of cap by scale precedes multiplication. This covers light falloff, display exposure, retained estimators and every valid positive bloom strength. The final tone consumer uses this helper, including subnormal strength, rather than the earlier temporary branch.

Ambient-only exposure independence remains a separate integration concern. The retained closed-form ambient estimate still applies its producing exposure before its estimator cap. Dividing that bounded estimate afterward cannot restore a clipped ambient value. A proposed scene-unit closed-form helper waits for issue #4's actual ambient-only extreme-history cut regression. This stage does not claim analytic camera-cut completion.
