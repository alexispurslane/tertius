# Research: temporal screen-space reflections and bounce light (ter-89xq)

A researcher (Codex GPT-6.1 SOL, reasoning effort xhigh) wrote this report on 2026-11-02 for the design of the temporal renderer. The orchestrator kept its content and changed only its punctuation to the project's writing rules. "Proposal" marks a design judgment or an unmeasured tuning value. Costs on the target devices and full MoltenVK and Linux compatibility remain unconfirmed. No repository file changed during the research.

Short answer: temporal sampling can replace much of today's blur, but it cannot recover geometry that is absent from the screen. The lowest-risk candidate is stochastic reflections plus a temporal version of the existing visibility-bitmask bounce estimate. A shared screen-probe system offers more reuse, but it needs a larger replacement.

One correction to the accumulator model matters. One shared disocclusion test can show whether the visible surface also existed last frame. Reflections also need checks on the reflected history, because surface motion alone does not describe reflection motion. AMD's denoiser compares the surface's reprojection with the reflection's reprojection. [1]

On 2026-11-03 the user chose a shared screen tracer for bounce light: diffuse rays through the same trace as the reflection rays. It is neither design of section 5. See `.tickets/ter-89xq.md`.

## 1. Stochastic reflections

AMD FidelityFX SSSR has five compute stages: depth hierarchy, tile classification, blue-noise generation, preparation of the indirect dispatch, and intersection. Its reflection denoiser adds Reproject, then Prefilter, then Temporal Resolve. The inputs are HDR scene color, depth, normals, roughness, motion vectors, camera transforms, environment maps and buffers of the previous frame. The SDK supports Vulkan and uses the MIT license. Neither component needs ray tracing hardware. [2, 3]

Proposal: a dense implementation can leave out the indirect scheduling. Its smallest useful sequence is depth hierarchy, stochastic trace, reprojection with statistics, spatial prefilter and temporal resolve. Each stage does not have to be a dispatch of its own.

SSSR samples the GGX reflection distribution, the range of directions that a rough surface reflects. Its hierarchical depth trace skips large empty regions. A miss reads the environment map at a mip level that follows the roughness. A configurable roughness threshold also sends very rough surfaces straight to the environment map. No method has a cutoff at 0.37. [4, 2]

"Half resolution" needs care. SSSR can trace one, two or four rays for each 2 x 2 group of pixels and then reconstruct the missing samples. Mirror-like surfaces get full-rate tracing. This differs from halving every buffer. [2]

Frostbite's 2015 method combines half-resolution tracing, full-resolution reconstruction, reuse of neighboring rays, temporal accumulation and full-resolution tracing for sharp reflections. Its example costs 2.19 ms on PS4: 0.99 ms of tracing, 1.07 ms of resolve and 0.13 ms of temporal filtering. These old numbers do not predict the M1 Max. The method also uses filtered scene color and a cubemap fallback. So stochastic tracing does not remove every radiance mip chain. [5]

Unreal's plain SSR documentation suggests a maximum roughness of about 0.8. Lumen's reflections use reflection rays, spatial reuse, temporal accumulation and a final bilateral filter. Its published design blends toward screen-probe light at roughness 0.3 to 0.4. Lumen's screen traces come before its world-space traces. Without world-space tracing, that fallback is gone, which is more than a quality setting. [6, 7]

To reproject a reflection, keep the hit distance and rebuild the virtual reflected point. AMD compares this candidate with the plain surface-motion reprojection, with checks of depth, normal, roughness and radiance. This handles parallax better than surface motion alone. It does not give exact motion for every moving reflected object. [1]

## 2. Screen-space bounce light

| Method | Image quality | Cost and complexity | Light from off screen |
|---|---|---|---|
| Visibility bitmask with temporal accumulation | Handles gaps and thin objects better than a single horizon. It keeps the biases of a finite thickness and of screen depth. Accumulation reduces the changing sampling pattern, not those biases. | Smallest change to Tertius. Therrien reports about 0.4 ms more than its GTAO comparison at 1080p on an RTX 2080. | Missing directions need an environment estimate. |
| Stochastic diffuse rays with denoising | Samples single light paths instead of angle sectors. It supports longer visible paths, but sparse samples give noise and unstable bright spots. | Proposal: moderate, because reflections can share the depth tracing. The trace cost depends strongly on ray length and scene depth. | A miss still needs an environment estimate. |
| Screen probes without world tracing | Shares directional light among nearby surfaces. It can give smooth diffuse light and rough reflections, but interpolation risks leaks and lost detail. | Highest: adaptive placement, directional storage, tracing, interpolation, spatial filtering and temporal reuse | Environment maps in place of world tracing do not reproduce full Lumen. |

The first row follows Therrien's paper. The other cost rankings are proposals, not measurements on one GPU. [8, 4, 7]

Lumen places probes on visible surfaces, adds probes around detailed geometry, and interpolates between compatible surfaces. Its published method uses the previous frame's probe light and a world-space radiance cache to guide sampling. So "Lumen's screen probe gather without world tracing" is a new, restricted design, not a proven equal. [7]

Tertius already uses visibility bitmasks and complementary environment light. The default near-light radius is one meter, in `src/Tertius.Rendering/ScreenSpaceLightSetting.cs:21`. Proposal: first make that estimate temporal and reduce its spatial filtering. This shows whether the complaint comes mainly from blur or from the estimate itself.

All three methods stay limited by what the screen shows. Environment maps centered on the camera give color from off screen, but a wrong parallax of the capture stays wrong after accumulation. This follows from the representation, not from a measurement.

## 3. The shared accumulator

Demodulation removes the receiving material's reflectance before filtering. Diffuse light uses the receiving surface's diffuse factor. Specular light uses an integrated specular factor that depends on material, view and roughness. Restore those factors after filtering. Do not remove the source object's color, because that color is part of the bounced light. NRD documents separate diffuse and specular factors. [9]

One sample per pixel does not give a reliable neighborhood of the final light in the current frame. A tight clamp of the history to those raw samples can throw away good accumulated light. ReLAX instead compares the slow history with statistics from a faster filtered history. History fix rebuilds recently reset pixels from their neighbors. When the light changes, anti-lag reduces the weight of old history. These mechanisms do different jobs. [10, 9]

Proposal: share the implementation, and keep separate state and policies for each signal:

- A slow history for stable light, and a fast history to detect changes.
- Rejection by depth, normal, material, the viewport's edge and camera cuts.
- For reflections, checks of the hit distance, the roughness and the virtual reflected point.
- Edge-aware spatial reconstruction for new or rejected pixels.
- A confidence value. When the light or the hit's reliability changes, it reduces the history's weight.

A shared surface test avoids repeated geometric work. It does not replace the reflection's own checks. "The receiving surface still exists" and "the same reflected content is still right" are different conditions. [1]

NRD exposes separate history limits for diffuse and specular light, fast histories and history-fix periods. Its defaults show that these controls matter, not that its values suit every engine. Proposal: start with about 16 to 32 frames for diffuse light and 8 to 16 for rough reflections. Then shorten the history for sharp reflections and for changes of light. These starting values are unmeasured. [11]

The per-signal filter and FSR can both delay a change. FSR cannot repair light that is already stale upstream. Its reactive mask reduces FSR's reliance on history. Its transparency and composition mask is meant for effects such as reflections that motion vectors describe poorly. Neither mask resets the signal's accumulator. Do not mark the whole screen reactive, because that gives up the reconstruction's stability. [12]

Proposal: keep the previous render size with each history. When the dynamic resolution changes, reproject with those sizes. Reject history that is newly exposed or incompatible.

## 4. Deterministic sampling

Spatiotemporal blue noise puts the error at frequencies that spatial and temporal filters remove well. NVIDIA publishes methods and source assets. Hashed sampling is also practical, but a deterministic hash alone does not give blue noise. Jarzynski and Olano compare the quality and cost of GPU hashes. [13, 14]

Proposal: choose samples from the fixed pixel coordinates, the frame number, a signal identifier and the sample dimension. Use distinct dimensions or scrambles for diffuse and specular rays.

Use FSR's jitter helper for the camera's sample position. Keep the ray sampling sequence under separate control, and rebuild positions consistently with the jittered camera. FSR's motion vector convention leaves out the camera jitter unless the integration turns on jitter cancellation. [12]

A fixed frame number fixes the choice of samples, not the temporal image. The same image also needs the same earlier frames, history resets, camera, exposure and sequence of render sizes. Bit-identical output across GPUs remains unconfirmed.

## 5. Two candidate designs

Both designs use one model: estimate the incoming light, use the environment's light for missing directions, filter demodulated signals, then apply the material's response.

| Point | A: separate estimators | B: shared screen-probe light |
|---|---|---|
| Reflections | SSSR-style rays, the demo's 0.38 paint included | Own rays for smooth reflections. Rough reflections sample the probes. |
| Bounce light | Temporal version of today's visibility-bitmask estimate | Diffuse light from the same directional probes that serve rough reflections |
| Work kept | The bitmask geometry, environment maps, the distance cube, the depth hierarchy's ideas | Environment maps, the distance cube, the depth hierarchy's ideas |
| Work replaced | The mirror-plus-cone reflection estimate and today's reflection reconstruction. Less spatial-only bounce filtering. | The visibility-bitmask bounce pipeline and the mirror-plus-cone reflections |
| Expected cost | Smaller implementation. Reflection work grows with the share of the screen that reflects. | More machinery and storage. Ray reuse can reduce repeated diffuse and rough-specular work. |
| Expected weakness | Bounce light keeps the sector and radius biases. | Probe interpolation can blur local detail and leak across geometry. |

These cost and quality comparisons are design inferences. Lumen gives evidence for sharing probe light with rough reflections, but its reported savings include a tracing system that Tertius will not have. [7]

The researcher recommended evaluating A first, without rejecting B. If measurements show that probe reuse pays for its storage and interpolation, B wins. Neither design by itself fixes the environment map's capture-space parallax. No primary source gives either design's cost on this M1 Max or on the required Vulkan drivers.

## Sources

1. [AMD reflection reprojection](https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v1.1.3/sdk/include/FidelityFX/gpu/denoiser/ffx_denoiser_reflections_reproject.h)
2. [AMD SSSR](https://gpuopen.com/manuals/fidelityfx_sdk/techniques/stochastic-screen-space-reflections/)
3. [AMD license](https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v1.1.3/LICENSE.txt)
4. [AMD intersection shader](https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v1.1.3/sdk/include/FidelityFX/gpu/sssr/ffx_sssr_intersect.h)
5. [Frostbite, Stachowiak 2015](https://www.slideshare.net/DICEStudio/stochastic-screenspace-reflections)
6. [Epic SSR](https://dev.epicgames.com/documentation/en-us/unreal-engine/screen-space-reflections-in-unreal-engine)
7. [Epic, Lumen 2022 presentation](https://advances.realtimerendering.com/s2022/SIGGRAPH2022-Advances-Lumen-Wright%20et%20al.pdf)
8. [Therrien et al.](https://arxiv.org/html/2301.11376v2)
9. [NVIDIA NRD](https://github.com/NVIDIA-RTX/NRD)
10. [ReLAX history clamping](https://raw.githubusercontent.com/NVIDIA-RTX/NRD/master/Shaders/RELAX_HistoryClamping.cs.hlsl)
11. [NRD parameters](https://raw.githubusercontent.com/NVIDIA-RTX/NRD/master/Include/NRDSettings.h)
12. [FSR 3.1 integration](https://gpuopen.com/manuals/fidelityfx_sdk/techniques/super-resolution-upscaler/)
13. [Spatiotemporal blue noise](https://research.nvidia.com/publication/2022-07_spatiotemporal-blue-noise-masks)
14. [GPU hashes](https://jcgt.org/published/0009/03/02/paper.pdf)
