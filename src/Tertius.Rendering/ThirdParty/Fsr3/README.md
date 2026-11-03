# AMD FidelityFX Super Resolution 3.1.4: the pinned GLSL

This folder holds the GLSL source of AMD's FSR 3.1 upscaler that the engine compiles, `Fsr3Upscaler` dispatches and `docs/plans/temporal-renderer.md`, section 5, specifies. The files are AMD's, from the FidelityFX SDK v1.1.4, with two changed declarations and one line of AMD's 3.1.6 fix. `LICENSE.txt` is AMD's MIT license, and every file keeps AMD's notice. `THIRD-PARTY-NOTICES.md` at the root carries the notice too.

## The pin

- Repository: https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK
- Tag: `v1.1.4`, commit `c6efa6bf7f2027b3ec94f28578bb5965eabb9e55`. Its upscaler is FSR 3.1.4 (`FFX_FSR3UPSCALER_VERSION_PATCH` 4 in `sdk/include/FidelityFX/host/ffx_fsr3upscaler.h`).
- The dev shell fetches the same commit as `TERTIUS_FFX_SDK` (`native/fidelityfx-sdk.nix`). `tools/fsr3-golden/` builds AMD's host code from it, and `tools/fsr3-golden/check-pin.sh` compares this folder with it.

The files keep AMD's paths below `sdk/`:

| Files | What they are |
|---|---|
| `sdk/src/backends/vk/shaders/fsr3upscaler/*.glsl` | AMD's ten Vulkan passes. The build compiles eight. The sharpening pass `rcas` and the debug view stay out, and they stay here as AMD ships them |
| `sdk/include/FidelityFX/gpu/fsr3upscaler/*.h` | The passes' code and their bindings (`ffx_fsr3upscaler_callbacks_glsl.h`), without AMD's HLSL bindings |
| `sdk/include/FidelityFX/gpu/ffx_core*.h`, `ffx_common_types.h` | AMD's portable shader types and functions |
| `sdk/include/FidelityFX/gpu/spd/ffx_spd.h` | AMD's single-pass downsampler, which the two luminance pyramids run |
| `sdk/include/FidelityFX/gpu/fsr1/ffx_fsr1.h` | FSR 1, which `ffx_fsr3upscaler_rcas.h` includes |
| `LICENSE.txt` | AMD's MIT license of the SDK |

## Why 3.1.4

FSR 3.1.4 is the last release with GLSL passes for Vulkan. The later releases are in the FidelityFX SDK 2: 3.1.5 in `v2.0.0` (commit `f4c1da8e92f3fe563b5c28c44e6267ce6b6b8eb2`) and 3.1.6 in `v2.4.0` (commit `7539ae8b6568ad29ed78a753404351daf3b5cca7`). Both ship the upscaler's shaders in HLSL only, with no `*glsl*` file under `Kits/`. So the engine pins the GLSL of 3.1.4 and compares the fixes of the two later releases with it:

| Release | Shader change against 3.1.4 | Effect on the engine's build |
|---|---|---|
| 3.1.5 | "Fix for possible negative RCAS output": `FsrRcasF` and its half variants in `fsr1/ffx_fsr1.h` multiply `hitMin` by `lowerLimiterMultiplier` | None. Only the sharpening pass runs RCAS, and the engine runs no sharpening and does not build that pass |
| 3.1.6 | `ReprojectHistoryColor` in `ffx_fsr3upscaler_reproject.h` passes `PreviousFrameUpscaleSize()` instead of `UpscaleSize()` to `HistorySample16` | None on the modules. The line is in the branch of `FFX_HALF && FFX_FSR3UPSCALER_OPTION_REPROJECT_SAMPLERS_USE_DATA_HALF`. The build sets `FFX_HALF=0`, and its 32-bit branch already passes `PreviousFrameUpscaleSize()` in 3.1.4. The pin carries the line all the same (see "AMD's 3.1.6 fix") |

Apart from those lines and the years of the notices, the 3.1.6 changes are in the host: messages, a watermark and D3D12 aliasing barriers. The engine repeats none of them, because its host is a C# copy of the 3.1.4 host (`Fsr3Upscaler`).

### How the shaders were compared

The comparison of 2026-11-03 fetched both commits into the Nix store and diffed each pinned header with its counterpart, ignoring white space and the lines of the notices. Run it again with these commands from the dev shell:

```bash
nix flake prefetch github:GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/f4c1da8e92f3fe563b5c28c44e6267ce6b6b8eb2
nix flake prefetch github:GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/7539ae8b6568ad29ed78a753404351daf3b5cca7
diff -w -I Copyright <pinned header> <counterpart>
```

The NAR hashes are `sha256-P6rvt15dDwxZCUV7M4LFR45ByNDnFq8ohthLsbdbcOM=` for v2.0.0 and `sha256-S/PxlzZwhF6sjTKwunxST5ZwQpqlYPiIWV9nuR7ksGM=` for v2.4.0. In both releases, the counterpart of `gpu/fsr3upscaler/`, `gpu/fsr1/` and `gpu/spd/` is under `Kits/FidelityFX/upscalers/fsr3/include/`, and the counterpart of the `gpu/ffx_core*.h` and `gpu/ffx_common_types.h` headers is under `Kits/FidelityFX/api/internal/`.

| Pinned header | 3.1.5 (v2.0.0) | 3.1.6 (v2.4.0) |
|---|---|---|
| `ffx_common_types.h`, `ffx_core.h`, `ffx_core_portability.h`, `ffx_core_gpu_common_half.h`, `spd/ffx_spd.h` | Equal | Equal |
| `ffx_core_gpu_common.h` | Three lines end with `;;` instead of `;`. No effect | The same |
| `fsr1/ffx_fsr1.h` | The RCAS fix above | The same as 3.1.5 |
| `fsr3upscaler/ffx_fsr3upscaler_rcas.h` | The include of `ffx_core.h` takes a longer path. No effect | The same |
| `fsr3upscaler/ffx_fsr3upscaler_reproject.h` | Equal | The reprojection fix above |
| The 12 other headers of `fsr3upscaler/` | Equal | Equal |
| `fsr3upscaler/ffx_fsr3upscaler_callbacks_glsl.h`, `ffx_core_glsl.h` | No counterpart: GLSL only | No counterpart |

The later releases replace the GLSL bindings with HLSL bindings only. So the comparison also diffed AMD's `ffx_fsr3upscaler_callbacks_hlsl.h` of 3.1.4 with those of both releases. The only change is the macro that declares a register. No binding, name or type changed.

## The changed declarations

AMD's GLSL declares two storage images in formats that differ from the images that AMD's host binds to them. The Vulkan spec leaves a store through such a mismatch undefined, and the validation layer reports it. `Fsr3UpscalerTests.UpscalingReportsNothingToValidation` reported both with AMD's declarations on MoltenVK:

```
Undefined-Value-StorageImage-FormatMismatch-ImageView: vkCmdDispatch(): the storage image descriptor [..., Set 0, Binding 7, Index 0, variable "rw_luma_history"] is accessed by a OpTypeImage that has a Format operand Rgba8 (equivalent to VK_FORMAT_R8G8B8A8_UNORM) which doesn't match the VkImageView ...[FSR3UPSCALER_LumaHistory2] format (VK_FORMAT_R16G16B16A16_SFLOAT). Any loads or stores with the variable will produce undefined values to the whole image (not just the texel being accessed).
Undefined-Value-StorageImage-FormatMismatch-ImageView: vkCmdDispatch(): the storage image descriptor [..., Set 0, Binding 2, Index 0, variable "rw_output_autoreactive"] is accessed by a OpTypeImage that has a Format operand R32f (equivalent to VK_FORMAT_R32_SFLOAT) which doesn't match the VkImageView ...[fsr reactive] format (VK_FORMAT_R8_UNORM). Any loads or stores with the variable will produce undefined values to the whole image (not just the texel being accessed).
```

Section 5 of the design gives the order of the fix. Its first rule takes the declaration of 3.1.5 or 3.1.6. Both declare the images in HLSL with no format, `RWTexture2D<FfxFloat32x4> rw_luma_history` and `RWTexture2D<float> rw_output_autoreactive`, so the first rule gives nothing. The second rule changes the one declaration to the format of the image:

| Image | AMD's declaration | The pin's declaration | The image's format |
|---|---|---|---|
| `rw_luma_history` | `rgba8` | `rgba16f` | `R16G16B16A16_FLOAT`, as AMD's host creates `FSR3UPSCALER_LumaHistory1` and `2` |
| `rw_output_autoreactive` | `r32f` | `r8` | `R8_UNORM`, the reactive mask of section 9, of AMD's manual and of AMD's sample |

With the two changes, the test reports no message.

## AMD's 3.1.6 fix

The pin carries one line of AMD's 3.1.6 fix (ticket `ter-7m38`, 2026-11-03). In the 16-bit branch of `ReprojectHistoryColor` in `ffx_fsr3upscaler_reproject.h`, `HistorySample16` takes `PreviousFrameUpscaleSize()` instead of `UpscaleSize()`. The line is that of `Kits/FidelityFX/upscalers/fsr3/include/gpu/fsr3upscaler/ffx_fsr3upscaler_reproject.h` in v2.4.0, and the rest of the file equals 3.1.4.

- The two sizes differ only in a frame after the output size changes. Then the 3.1.4 line reads the history at the new size, which is AMD's defect.
- The 32-bit branch had the fix in 3.1.4. The engine's build compiles that branch, so its modules did not change, byte for byte.
- The port keeps a later 16-bit build from carrying the known defect.

`tools/fsr3-golden/pin.diff` is the whole difference from AMD's tag: the two declarations and this line.

## The build

`Tertius.Rendering.csproj` compiles the eight passes that the engine dispatches. It uses `glslangValidator` from the dev shell, as AMD's build does, and checks each module with `spirv-val`:

```
glslangValidator -V --target-env vulkan1.3 -S comp -Os -DFFX_GPU=1 -DFFX_GLSL=1 -DFFX_HALF=0 -DFFX_SPD_NO_WAVE_OPERATIONS=1 \
  -DFFX_FSR3UPSCALER_OPTION_UPSAMPLE_SAMPLERS_USE_DATA_HALF=0 -DFFX_FSR3UPSCALER_OPTION_ACCUMULATE_SAMPLERS_USE_DATA_HALF=0 \
  -DFFX_FSR3UPSCALER_OPTION_REPROJECT_SAMPLERS_USE_DATA_HALF=1 -DFFX_FSR3UPSCALER_OPTION_POSTPROCESSLOCKSTATUS_SAMPLERS_USE_DATA_HALF=0 \
  -DFFX_FSR3UPSCALER_OPTION_UPSAMPLE_USE_LANCZOS_TYPE=2 -DFFX_FSR3UPSCALER_OPTION_REPROJECT_USE_LANCZOS_TYPE=0 \
  -DFFX_FSR3UPSCALER_OPTION_HDR_COLOR_INPUT=1 -DFFX_FSR3UPSCALER_OPTION_LOW_RESOLUTION_MOTION_VECTORS=1 \
  -DFFX_FSR3UPSCALER_OPTION_JITTERED_MOTION_VECTORS=0 -DFFX_FSR3UPSCALER_OPTION_INVERTED_DEPTH=1 \
  -DFFX_FSR3UPSCALER_OPTION_APPLY_SHARPENING=0 -I gpu -I gpu/fsr3upscaler <pass>.glsl
```

- `FFX_HALF=0` and `FFX_SPD_NO_WAVE_OPERATIONS=1` give the portable build: 32-bit floats, and a downsampler that uses shared memory instead of subgroup operations.
- The first five options are AMD's base arguments (`CMakeCompileFSR3UpscalerShaders.txt`). `REPROJECT_SAMPLERS_USE_DATA_HALF=1` has no effect without `FFX_HALF`.
- The other options choose AMD's permutation for the engine's inputs: HDR color, motion vectors at the render size without jitter, reversed depth, and no sharpening.
- Lanczos: the upsample uses AMD's approximation (type 2, AMD's base argument), and the reprojection the reference function (type 0). AMD's host picks type 1, a lookup table, only on a device whose waves have from 32 to 64 lanes (`ffx_fsr3upscaler.cpp`, `createPipelineStates`). So the engine creates and binds no `FSR3UPSCALER_LanczosLutData`.
- The target is Vulkan 1.3, the engine's. AMD's build targets Vulkan 1.2.

The modules use these SPIR-V capabilities: `Shader`, `ImageQuery`, `StorageImageExtendedFormats`, and `StorageImageWriteWithoutFormat` in the accumulate pass, which writes the output through `rw_upscaled_output`, declared with no format. No module uses a subgroup or a 16-bit capability. The engine requires `shaderStorageImageExtendedFormats` and `shaderStorageImageWriteWithoutFormat` (ARCHITECTURE.md, "Required device features").

The images that the shaders filter with AMD's linear sampler are of formats whose linear filtering Vulkan requires: `R8_UNORM`, `R8G8B8A8_UNORM`, `R16_SFLOAT`, `R16G16_SFLOAT` and `R16G16B16A16_SFLOAT`. No pass filters a 32-bit float image.

## The 16-bit and subgroup builds

Ticket `ter-7m38` (2026-11-03) built three variants beside the build above, tested them and timed them. The engine keeps the build above for now, and no switch chooses a build at run time. Whether a variant replaces it depends on which median the ticket's rule means, and the user did not choose yet ("The timing" below).

### How the variants follow AMD

- AMD's host asks for its 16-bit modules, `FFX_HALF=1`, in four passes only: autogen reactive, prepare reactivity, shading change and accumulate. The other passes always run AMD's 32-bit modules (`sdk/src/backends/shared/blob_accessors/ffx_fsr3upscaler_shaderblobs.cpp` of v1.1.4). So the 16-bit variants set `FFX_HALF=1` in those four passes only.
- The 16-bit branch of the luminance pyramid in `ffx_fsr3upscaler_luma_pyramid.h` loads 0 and stores nothing. A build with `FFX_HALF=1` in every pass failed the binding test, the scene change test and the snapshot test.
- `ffx_spd.h` tests `defined(FFX_SPD_NO_WAVE_OPERATIONS)`, not its value. So the subgroup variant leaves the define out, as AMD's own Vulkan build does. With `FFX_SPD_NO_WAVE_OPERATIONS=0`, glslang gives the same module as with `1`.

### Capabilities and device requirements

`spirv-dis` lists these capabilities beyond those of the build above:

| Build | Defines | Changed modules | Added capabilities | Vulkan requirement |
|---|---|---|---|---|
| 16-bit | `FFX_HALF=1` in AMD's four 16-bit passes | Autogen reactive, prepare reactivity, shading change, accumulate | `Float16` (accumulate), `Int16` (accumulate, autogen reactive) | The features `shaderFloat16` and `shaderInt16` |
| Subgroup | No `FFX_SPD_NO_WAVE_OPERATIONS` | The two pyramids | `GroupNonUniform`, `GroupNonUniformQuad` | `subgroupSupportedStages` with the compute stage, and `subgroupSupportedOperations` with the basic and the quad operations. Vulkan 1.1 requires the compute stage and the basic operations of a device with a compute queue, so only the quad operations are new |
| Both | Both of the above | The six modules above | All four | All of the above |

- No module uses a capability of 16-bit storage, such as `StorageBuffer16BitAccess`. `ffx_core_glsl.h` enables `GL_EXT_shader_16bit_storage`, but no pass reads or writes 16-bit values in a buffer or a shader interface. So no build needs `storageBuffer16BitAccess` or its siblings.
- No build needs `shaderSubgroupExtendedTypes`. The pyramids stay 32-bit in AMD's 16-bit build, so their quad swaps take 32-bit values. With `FFX_HALF=1` in the pyramids, they swap `v4half` values and need the feature.

Support on the user's list of GPUs, from each driver's source:

| Requirement | NVIDIA's driver and NVK, Turing and later | RADV: Vega and RDNA | ANV: Gen9 and later | MoltenVK | lavapipe |
|---|---|---|---|---|---|
| `shaderFloat16` | NVIDIA's driver reports it on Turing. NVK sets it for `sm >= 70`, and Turing is SM 7.5 | Set from `has_packed_math_16bit`, which is `gfx_level >= GFX9`: Vega and later | Set unless the debug switch `no_16bit` is on | Always set | Set from llvmpipe's fp16 cap: an x86 CPU with F16C, or any AArch64 CPU |
| `shaderInt16` | NVIDIA's driver reports it on Turing. NVK always sets it | Always set | Always set | Always set | Set from llvmpipe's int16 cap, which is always on |
| Quad operations in the compute stage | NVIDIA's driver reports them on Turing, with the compute stage. NVK sets them in every supported stage | Set | Set, in every stage | Set on the Apple4 and Mac2 GPU families | Set with LLVM 10 or later, and the stages include compute |

The sources are Mesa's main branch at `b381eaf859621ed7fdb3bfef00ac956e4cca209c` (2026-11-03): `src/nouveau/vulkan/nvk_physical_device.c`, `src/amd/vulkan/radv_physical_device.c`, `src/amd/common/ac_gpu_info.c`, `src/intel/vulkan/anv_physical_device.c`, `src/gallium/frontends/lavapipe/lvp_device.c` and `src/gallium/auxiliary/gallivm/lp_bld_limits.h`. MoltenVK's are `MoltenVK/MoltenVK/GPUObjects/MVKDevice.mm` at `5189f8de86ec365860f63bf1da736815cfaea5cd`. NVK is Mesa's driver for NVIDIA GPUs. NVIDIA's own driver has no public source. Its answers come from three vulkan.gpuinfo.org reports of Turing GPUs on Linux, which the Khronos repository `KhronosGroup/Vulkan-Profiles` stores at commit `a33e51f81836dcc7d75986bebeceab2178433db2`, under `profiles/LunarG/`:

- GeForce GTX 1650, driver 610.43.02, Ubuntu 24.04: `VP_LUNARG_desktop_baseline_2026/vp_gpuinfo_nvidia_geforce_gtx_1650_610_43_2_0_ubuntu_24_04.json`.
- GeForce RTX 2060, driver 525.47.26, Ubuntu 23.04: `VP_LUNARG_desktop_max_2024/vp_gpuinfo_nvidia_geforce_rtx_2060_525_47_26_0_ubuntu_23_04.json`.
- GeForce RTX 2070, driver 610.43.02, Arch Linux: `VP_LUNARG_desktop_max_2026/vp_gpuinfo_nvidia_geforce_rtx_2070_610_43_2_0_arch_unknown.json`.

Each report has the driver ID `VK_DRIVER_ID_NVIDIA_PROPRIETARY`, `shaderFloat16` and `shaderInt16` true, `VK_SUBGROUP_FEATURE_QUAD_BIT` in `supportedOperations`, and `VK_SHADER_STAGE_COMPUTE_BIT` in `supportedStages`. vulkan.gpuinfo.org itself refused automated reads. The Apple M1 Max reports every requirement (`vulkaninfo`, MoltenVK 1.4.2).

### Correctness

Each variant ran every test of `Fsr3HostTests`, `Fsr3UpscalerTests` and `DeviceFeatureTests` in Release, under the validation layer with synchronization validation. Every FSR test passed with no message. The reference image of `TheSettledPatternAndTheFramesAfterAChangeOfTheSceneMatchTheirReferenceImage` gives these largest FLIP values, against the tolerance of 0.1:

| Build | Largest FLIP value | Mean FLIP value |
|---|---|---|
| The build above | 0.0000 | 0.000000 |
| 16-bit | 0.0134 | 0.000127 |
| Subgroup | 0.0000 | 0.000000 |
| Both | 0.0134 | 0.000127 |

### The timing

The opt-in test `AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime` (`TERTIUS_FSR_TIMING=1`) times 300 frames from 1280 × 720 to 1920 × 1080 by timestamps around `Record`. It ran from five Release builds of the test project in 60 rounds, on the Apple M1 Max:

- The builds differ only in `Tertius.Rendering.dll`. The control is a byte copy of the build above, so that the table shows the spread between two runs of the same code.
- Each round runs each build once, in an order that rotates by one build per round.
- Each run takes the GPU lock, `/tmp/tertius-gpu-measure.lock`, and releases it as soon as the run ends. Each run records `uptime` at its start.
- The 1-minute load at the start of the runs was 3.4 at the least, 6.0 at the median and 30.5 at the most, because other agents built at the same time.

The tool `tools/fsr3-timing/` builds the variants, runs the rounds and computes the numbers below (`tools/README.md`, "FSR 3.1 timing"). The frame times of the 60 rounds lie in `/private/tmp/ter-7m38-timing`, which the machine clears.

The times of the frames of one build have several modes. For the build above, the peaks lie near 2.6, 3.0 and 3.45 ms. So one median of all frames depends on the share of frames in the slow modes, and this share changes from run to run. The ticket's rule, "the variant's median is lower than today's by more than the control's spread", therefore has two readings, and they give different results.

| Build | Median of all frames, ms | 10th percentile, ms | 90th percentile, ms | Median of the 60 run medians, ms |
|---|---|---|---|---|
| The build above | 2.826 | 2.581 | 3.446 | 2.725 |
| Control, a byte copy | 2.769 | 2.580 | 3.440 | 2.711 |
| 16-bit | 2.797 | 2.547 | 3.416 | 2.744 |
| Subgroup | 2.788 | 2.546 | 3.375 | 2.702 |
| Both | 2.776 | 2.521 | 3.348 | 2.684 |

The two readings of "median minus the build above", each as the middle value and the 90 % interval of 2,000 resamples of the rounds, in ms:

- The pooled frame median is the median of all 18,000 frames of a build. Each resample draws whole rounds with replacement and pools their frames.
- The paired run median takes, in each round, the median of the build's run minus the median of the run of the build above in the same round. Then it takes the median of these 60 differences. Each resample draws the differences of whole rounds with replacement.

| Build | Pooled frame median | Paired run median | Rounds in which the run median was lower |
|---|---|---|---|
| Control, a byte copy | −0.047, from −0.178 to +0.066 | +0.024, from −0.006 to +0.038 | 25 of 60 |
| 16-bit | −0.030, from −0.163 to +0.099 | −0.025, from −0.043 to +0.016 | 34 of 60 |
| Subgroup | −0.038, from −0.159 to +0.075 | −0.061, from −0.082 to −0.026 | 39 of 60 |
| Both | −0.052, from −0.168 to +0.061 | −0.085, from −0.098 to −0.049 | 39 of 60 |

By the pooled frame median, the control came out 0.057 ms faster than the build above, and its interval reaches 0.178 ms below it. No variant gains more than that, so no variant is measurably faster by this reading. The ticket fixed this reading after round 19 of 60, when the per-round differences of the run medians were already in its output.

By the paired run median, the control lies within 0.038 ms of the build above. The subgroup build gains 0.061 ms and the build with both changes 0.085 ms, 3.1 % of the frame. The interval of each lies wholly below the control's. So by this reading, both builds are measurably faster, and the rule keeps the faster one, the build with both changes.

At the 10th percentile, the fast frames, the build with both changes gains in both readings. By the pooled 10th percentile, it is 0.061 ms faster, from −0.070 to −0.022 ms, and the control's interval is from −0.043 to +0.038 ms, so the two intervals overlap from −0.043 to −0.022 ms. By the paired run 10th percentile, the build with both changes is 0.073 ms faster, from −0.076 to −0.067 ms, in 49 of 60 rounds, and the control is within 0.005 ms.

The order of the builds in a round rotates by one build per round. So in 48 of 60 runs, each build follows the same build. An effect of one run on the next cannot be told apart from an effect of the build. The control and the build above follow different builds and differ by 0.002 ms at the paired run 10th percentile, so such an effect looks small here.

## Bindings

Every pass uses set 0, and AMD's linear clamp sampler at binding 1001. glslang removes unused bindings, so a pass binds only what it reads or writes. `Fsr3Bindings` holds the same table, and `Fsr3HostTests.EveryPassBindsWhatItsCompiledModuleDeclaresUnderAmdsNames` compares it with the binding numbers and the variable names of each embedded module:

| Pass | Sampled images | Storage images | Uniform buffers |
|---|---|---|---|
| `autogen_reactive` | 0 opaque only, 1 color | 2 reactive mask `r8` | 5 `cbGenerateReactive` |
| `prepare_inputs` | 0 motion vectors, 1 depth, 2 color | 3 dilated motion `rg16f`, 4 dilated depth `r32f`, 5 reconstructed previous depth `r32ui`, 6 farthest depth `r16f`, 7 current luma `r16f` | 8 `cbFSR3Upscaler` |
| `luma_pyramid` | 0 current luma, 1 farthest depth | 2 atomic counter `r32ui`, 3 frame info `rgba32f`, 9 pyramid level 5 `rg16f`, 10 farthest depth level 1 `r16f` | 11 `cbFSR3Upscaler`, 12 `cbSPD` |
| `shading_change_pyramid` | 0 current luma, 1 previous luma, 2 dilated motion, 3 exposure | 4 atomic counter `r32ui`, 5 to 10 pyramid levels 0 to 5 `rg16f` | 11 `cbFSR3Upscaler`, 12 `cbSPD` |
| `shading_change` | 0 pyramid | 1 shading change `r8` | 2 `cbFSR3Upscaler` |
| `prepare_reactivity` | 0 reconstructed previous depth, 1 dilated motion, 2 dilated depth<br>3 reactive mask, 4 transparency and composition mask<br>5 accumulation, 6 shading change, 7 current luma, 8 exposure | 9 dilated reactive masks `rgba8`, 10 new locks `r8`, 11 accumulation `r8` | 12 `cbFSR3Upscaler` |
| `luma_instability` | 0 exposure, 1 dilated reactive masks, 2 dilated motion, 4 luma history, 6 current luma | 7 luma history `rgba16f`, 8 luma instability `r16f` | 9 `cbFSR3Upscaler` |
| `accumulate` | 0 exposure, 1 dilated reactive masks, 2 dilated motion, 3 upscaled history, 5 farthest depth level 1, 7 luma instability, 8 color | 9 upscaled history `rgba16f`, 10 output (no format), 11 new locks `r8` | 12 `cbFSR3Upscaler` |

The shading change pyramid binds seven storage images, the most of any pass, and prepare reactivity samples nine images. `Fsr3Upscaler` refuses a device whose compute stage sees fewer descriptors of a type than a pass binds, by the engine's per-stage check (`LimitedDescriptors`).
