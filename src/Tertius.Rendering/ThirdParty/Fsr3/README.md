# AMD FidelityFX Super Resolution 3.1.4: the pinned GLSL

This folder holds the GLSL source of AMD's FSR 3.1 upscaler that the engine compiles, `Fsr3Upscaler` dispatches and `docs/plans/temporal-renderer.md`, section 5, specifies. The files are AMD's, from the FidelityFX SDK v1.1.4, with two changed declarations. `LICENSE.txt` is AMD's MIT license, and every file keeps AMD's notice. `THIRD-PARTY-NOTICES.md` at the root carries the notice too.

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
| 3.1.6 | `ReprojectHistoryColor` in `ffx_fsr3upscaler_reproject.h` passes `PreviousFrameUpscaleSize()` instead of `UpscaleSize()` to `HistorySample16` | None. The line is in the branch of `FFX_HALF && FFX_FSR3UPSCALER_OPTION_REPROJECT_SAMPLERS_USE_DATA_HALF`. The build sets `FFX_HALF=0`, and its 32-bit branch already passes `PreviousFrameUpscaleSize()` in 3.1.4 |

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

`tools/fsr3-golden/pin.diff` is the whole difference. With the two changes, the test reports no message.

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
