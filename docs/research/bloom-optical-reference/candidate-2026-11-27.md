# Measured bloom candidate

The candidate preserves source radiance and the selected normal glow. It compresses extreme glare after exposure. This is an artistic response; the optical references in [README.md](README.md) characterize its normal energy and angular falloff independently. The verified common source-unit commit is adopted. Final integration checks, independent review and Linux cost measurements remain pending.

## Source and halo measurements

[The measurement record](measurements-2026-11-27.json) contains the full numerical reports. The test renders a 24 × 24 output-pixel source at 640 × 360 pixels, with a 60-degree vertical field of view and fixed exposure 0 stops. The square subtends 0.00591716 sr. Threshold is 1, knee is 0.5, strength is 0.25, and the filter uses six levels.

Source radiance is the largest RGB channel at the center, in W/(m² sr). Halo energy below is the sum of linear Rec. 709 radiance outside the square plus a two-output-pixel margin, multiplied by strength. Each half-resolution sample represents four output pixels. Its units are radiance times output pixels; it is not an angular integral or a physical scattered-energy fraction.

| Source | Original radiance | Twice original radiance | Original halo energy | Twice-input halo energy |
|---|---:|---:|---:|---:|
| Emissive lamp | 64 | 128 | 4875.397706866264 | 9820.969826459885 |
| Matte panel | 50.8674 | 101.7348 | 3718.9539046889345 | 7513.734255406163 |
| Glossy panel | 36.699318 | 73.398636 | 2544.948612602219 | 5163.585124646898 |

Both the default key-and-fill path and the path that evaluates every light pair produce these halo measurements. The source centers display white in both inputs. Their slightly greater than twofold halo ratios follow subtraction of the threshold before filtering. The original source tests retain the 1,000× input assertions: lamp 64,000, matte panel 50,867.406, and glossy panel 36,699.316. The artistic response follows that preserved source.

The normal lamp's mean halo radiance in output-pixel rings [24,48), [48,96), and [96,192) is 0.29432538688007004, 0.05976837877851246, and 0.008714052630892876. The image crops the last ring vertically. These values and the normal outside-source sum exactly match the saved baseline. The independent optical reference remains the finite-square calculation in README.md; no observer age or optical acceptance tolerance has been selected.

At lamp radiances 64,000 and 640,000, measured halo energies are 19771.8295058012 and 19802.888367056847. Their ratio is 1.0015708643070451. The two clipped white cores therefore retain a measurable halo difference through half-float filtering.

## Artistic response and finite arithmetic

Let excess brightness mean the largest exposed color channel after the existing soft threshold. The response equals excess brightness through 128. Above 128, it adds 128 times the ratio of excess above 128 to that excess plus 128. It approaches 256 continuously, with matching first derivatives at the join. RGB channels retain their ratio to the largest channel.

Exposure acts only after the meter reads the original source. The source operand is limited to 2^48 divided by the exposure scale before multiplication, so the derived display value cannot overflow. The original source remains in RGBA32F with the separate source bound documented in [storage.md](storage.md).

Each thresholded channel is at most 256. Positive normalized filter weights preserve that bound. The prefilter divides by the number of levels before the additive up chain. There are at most 14 levels. A conservative multiplicative half-float rounding bound of (1 + 2^-10)^42, covering downsampling, upsampling and addition at every level, keeps a channel below 267. The half-float maximum is 65,504. The largest native weighted sum is 128 × 256 = 32,768 in float32 before division. The mapped path uses the same normalized filter on thresholded source texels. No half-float attachment saturation is required.

## Matched real scenes

| Control | Simulation tick | Output pixels | Active render pixels | Applied exposure, stops | Validation messages |
|---|---:|---|---|---:|---|
| Helmet, bloom on | 94,901 | 1280 × 720 | 848 × 477 | 6.0043726 | None |
| Settled exterior, bloom on | 177,902 | 960 × 540 | 640 × 360 | 4.17438 | None |
| Settled exterior, bloom off | 177,902 | 960 × 540 | 640 × 360 | 4.17438 | None |

The helmet region has original maximum radiance 0.58125883 W/(m² sr). After exposure its maximum is 37.313484, below the artistic join. The helmet keeps its gold rim and local glow. The console and cabin remain readable.

The settled exterior pair has identical original-source and reconstructed-image summaries. Its original maximum radiance is 1578.6647 W/(m² sr), and the exposed maximum is 28503.846. The bloom-on image retains the nose, upper hull panels and two bright lower strips. Its remaining glow is visible against the dark hull and sky.

The continuous exterior comparison contains 800 displayed frames. Applied exposure float32 bits match the bloom-off control at every frame. Lossless samples at frames 500, 600 and 700 preserve the original captures for comparison. Frames 500 and 600 and the helmet received an independent qualitative inspection from the coordinator. That inspection covers those frames; it is not approval of every frame in the movie.

The rejected response that stayed linear through 1024 spread a broad white veil over the hull. At frame 500, within output rectangle x=280, y=200, width=320, height=190 pixels, the bloom-off image has 725 white pixels, the rejected response has 2700, and the candidate has 897. These are descriptive image measurements, not an invented acceptance threshold.

Evidence lives under `/Users/alexispurslane/Movies/Blue Danube/exposure-bloom-recovery/`. The continuous candidate is `issue-3-candidate-128-on/egress.mp4`; the matching bloom-off control is `issue-3-candidate-1024-off/egress.mp4`. Each folder retains three lossless PNG samples. The settled pair is in `issue-3-candidate-128-settled-on` and `issue-3-candidate-128-settled-off`. The fixed-exposure helmet is in `issue-3-candidate-128-helmet-on`.

## Verification to date

- The original focused source and motion suite passed 19 cases, including both direct-light paths, the 1,000× original-source assertions, both exposure endpoints and the retained extreme halo distinction.
- The new 640-to-6400 lamp probe first failed the rejected response with halo ratio 2.9490578678794983, against [1.01,2], before the candidate passed it.
- The separate source-report and coverage run passed 16 cases. It collected numerical artifacts after correcting report filenames to keep both direct-light paths separate.
- The continuous candidate, fixed-exposure helmet and each settled control passed their explicit fixture with no Vulkan validation messages.
- The 22-case bloom class passed after rescaling the extreme-square probe strength to 1/256. Its visible-halo assertion is unchanged, and its finite-level bound tightened to 512.

The final runtime change adopts the verified common source/exposure boundary. C# coverage is retained, but a changed-line percentage is not yet meaningful against the committed base. Shader branches require the scripted mutants and arithmetic proofs; C# coverage does not instrument GLSL. No Mac timing is a performance result.

## Scripted mutation results

The [mutation record](hardening-2026-11-27.json) gives one final outcome per mutant, the staged source hashes, the original run and the scoped rerun. The original run took 325 seconds. The B09 rerun took 34 seconds. Both runners verified source bytes, modes, semantic index entries and HEAD, then rebuilt the plain assembly. No mutant was applied or restored by hand.

| Mutant | Changed behavior | Final result | Evidence or disposition |
|---|---|---|---|
| B01 | Remove extreme compression | Killed | The 640-to-6400 glare-growth test fails. |
| B02 | Replace the response with hard saturation | Killed | Distinct extreme halos and moderate extreme growth fail. |
| B03 | Clip the original bloom source at 32,256 | Killed | The 64,000-to-640,000 halos become indistinguishable. |
| B04 | Omit the soft knee and extreme response | Killed | The independent knee reference and visible extreme-halo probes fail. |
| B05 | Reverse source jitter compensation | Killed | Halo energy varies under half-size temporal upscaling. |
| B06 | Lose one bilinear source corner | Killed | Halo energy varies under native-size and half-size temporal upscaling. |
| B07 | Omit division by the number of levels | Killed | The chain reference and large-source energy tests fail. |
| B08 | Restore the subnormal knee divisor | Survived, judgment call | Output-only tests on this Mac do not expose subnormal intermediate arithmetic. The finite-divisor proof and half-float-store ordering remain required. This is not an equivalent-mutant classification. |
| B09 | Ignore horizontal render scale | Killed after a new regression test | The original energy test missed a displaced halo. The new light-center test fails at x=160.34312314056598 against x=320.25 ± 2 output pixels. |
| B10 | Omit horizontal source jitter | Killed | Halo energy varies under half-size temporal upscaling. |

The new B09 test computes the center of the halo's linear light from output pixel positions. One active source pixel spans 1/scale output pixels. The expected source position comes from the scene fixture, independently of the tested coordinate conversion. The baseline passed all five motion and light-center cases before the B09 mutant failed. The original energy tests remain unchanged.

The final outcome is nine killed and one survived with a recorded judgment call. There were no timeouts, failed builds, empty test selections or equivalent-mutant classifications. No whole-feature mutation score is claimed for source writers or common exposure consumers; this run covers the bloom shader and pass.

## Source and storage hardening

The [source hardening record](source-hardening-2026-11-28.json) covers 22 selected faults in source writers, fresh ambient conversion and image formats. The first run caught 19 faults. Three survived because the selected fixtures did not check ambient light at a traced hit, the absolute brightness of broad reflected volume light, or atlas emission above the half-float range.

Four new cases pass on the unmodified renderer. A scoped scripted rerun catches all three surviving faults:

| Fault | Rendered failure |
|---|---|
| Double ambient light at a traced hit | A mirror sample reads 0.99853516 instead of 0.50001717 at zero exposure stops. The four-stop case fails too. |
| Double broad reflected volume light | A white metal returns 9.34669 instead of the 4.671875 arriving at its surface. Its unit albedo requires one copy. |
| Restore half-float atlas color | Captured emission stops at 65,504 instead of retaining 262,144 before display exposure. |

The final result is 22 caught faults, with no surviving faults, timeouts, build failures or empty test selections. The [initial list](source-mutants-2026-11-28.json) and [scoped list](source-survivor-mutants-2026-11-28.json) reproduce the runs through `tools/mutate.py`. Both runs verify source bytes, file modes, the Git index and HEAD, then rebuild the unmodified assembly. This stage changes tests and evidence only.

## Linux measurement status

The [Linux record](linux-cost-2026-11-28.json) preserves two failed attempts. The first stopped before rendering because the canonical models were absent. The second provisioned the assets shell, then the primary NOAA terrain download timed out during its TLS handshake. Neither attempt produced frame-time measurements or observed render dimensions. Their total accounted cost is $0.0525.

The asset sidecar already approves a hash-identical NOAA staging mirror. A fresh download matches all 9,370,338 bytes and the pinned SHA-256. One further attempt is approved to fetch that mirror on the worker, verify its pin and pass it to the existing asset setup command. The measurement retains the same immutable commits, 840-second limit and matched settings. Performance remains unmeasured until that attempt succeeds.
