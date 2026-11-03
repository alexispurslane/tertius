# Research: the FSR 3.1 integration path (ter-89xq)

A researcher (Codex GPT-6.1 SOL, reasoning effort xhigh) wrote this report on 2026-11-02 for the design of the temporal renderer. It is kept as written, apart from this header. Claims that the researcher marks as unconfirmed stay unconfirmed. No repository file changed during the research, and its temporary downloads were removed.

Short answer: FSR 3.1 upscaling looks feasible for Tertius through portable Vulkan, but AMD's host library is not a drop-in Linux/macOS dependency. The older SDK provides usable GLSL sources. The researcher compiled representative shaders and ran spirv-val on them, but also found a shader/host format mismatch that the integration must resolve. Correct rendering on MoltenVK remains unconfirmed.

## 1. Source form and license

AMD still uses the `GPUOpen-LibrariesAndSDKs/FidelityFX-SDK` repository, although the current product is named AMD FSR SDK. Distinguish the SDK version from the upscaler version. [1–3]

| SDK release | FSR upscaler | Source form |
|---|---|---|
| FidelityFX SDK `v1.1.4` | `3.1.4` | GLSL and HLSL |
| FidelityFX SDK `v2.0.0` | `3.1.5` | Newer SDK layout |
| FSR SDK `v2.4.0`, October 28, 2026 | `3.1.6` | HLSL sources and Vulkan backend sources |

In `v1.1.4`, Vulkan shader entry points live under `sdk/src/backends/vk/shaders/fsr3upscaler/`. Shared implementation and GLSL resource declarations live under `sdk/include/FidelityFX/gpu/fsr3upscaler/`. AMD's Vulkan build explicitly uses glslang, targeting Vulkan 1.2. DXC is not required for this route. [2]

In `v2.4.0`, the upscaler lives under `Kits/FidelityFX/upscalers/fsr3/`. Its source tree contains HLSL, not the older GLSL entry points. The release restores Vulkan feature parity. Unconfirmed: no reproducible source-build recipe was established for compiling this release's HLSL into its shipped Vulkan shaders. [1,3]

The FSR 3 source files are MIT-licensed. Preserve their copyright and permission notices. The current SDK's default binary redistribution license differs, but its license file explicitly lists the FSR 3 sources among the MIT exceptions. Do not describe the entire current SDK as MIT. [4]

## 2. Host side and existing integrations

AMD's unmodified `ffx-api` package is Windows-oriented, even for Vulkan. In `v1.1.4`, its build links `DXGI`, its exports use `__declspec(dllexport)`, and its loader includes `windows.h`. The Vulkan backend also uses Microsoft-specific C runtime functions. A native port is possible, but requires source and build changes. [5]

Precedents:

- Godot retains AMD's FSR 2 effect logic and implements its backend callbacks through Godot's own `RenderingDevice`. Resource creation, pipeline creation and compute execution use Godot code rather than AMD's Vulkan backend. [6]
- An X4 developer reports compiling FSR 3 on Linux after wrapping `_countof`, `wcscpy_s` and `sprintf_s`. This is a first-party integration report, not an independent execution test. [7]
- `fsr3-native-macos` supplies an Apple Silicon patch for SDK `v1.1.4`. It ports shader tooling and host code, builds an upscaler library, and excludes Windows-bound frame-generation presentation code. Its author reports MoltenVK use. Correctness and performance remain independently unconfirmed. [8]

A custom host needs more than shader dispatches:

- Resources:
  - paired color, luminance and accumulation histories
  - depth and motion intermediates
  - reactivity, shading-change and lock images
  - mip chains and an atomic counter
  - exposure and frame information, defaults and lookup data.
- Constant buffers: the main FSR constants, downsampling constants, sharpening constants and reactive-mask generation constants.
- Per-view state: history selection, frame index, current and previous dimensions, jitter, pre-exposure and reset state. Preserve exact shader layouts, clear operations, resource bindings and memory dependencies. [9–11]

AMD's host implementation defines the dispatch order and dimensions. Reactive-mask generation is separate from the main upscaler dispatch. Optional sharpening and debug output add work. [9]

## 3. Shader requirements and portability

The researcher downloaded about 732 KB of upstream source. In Tertius's Nix shell, all ten GLSL entry points compiled in three configurations: FP32, FP16, and FP32 without subgroup operations. All 30 resulting SPIR-V modules passed `spirv-val`. These are compilation tests, not GPU execution tests. A subgroup is a set of shader invocations that exchange values directly.

| Requirement | Observed shader use | Local M1 Max / MoltenVK 1.4.2 |
|---|---|---|
| Extended storage-image formats | FP32 and FP16 paths | Supported |
| Storage-image writes without a declared format | Output-writing passes | Supported |
| Subgroup quad operations | Default downsampling passes | Supported, subgroup size 32 |
| `shaderFloat16` and some `shaderInt16` use | FP16 variants | Supported |
| Integer storage-image atomics | `R32_UINT` intermediates | Supported |

The local driver also reports 16-bit buffer storage, extended subgroup types, and storage-image reads without format. However, the tested shaders emitted no `StorageImageReadWithoutFormat` capability and no 16-bit buffer-storage capability. A GLSL extension directive alone does not establish an emitted SPIR-V requirement.

The inspected storage formats include `R8_UNORM`, `RGBA8_UNORM`, `R16_SFLOAT`, `RG16_SFLOAT`, `RGBA16_SFLOAT`, `R32_SFLOAT`, `RGBA32_SFLOAT` and `R32_UINT`. Local format queries report the needed storage support, including atomics for `R32_UINT`.

Mesa's lavapipe source supports the relevant image operations and subgroup quad operations. Its subgroup width depends on its CPU vector width. Its FP16 support depends on CPU support, including x86 F16C or AArch64. FP16 must therefore remain optional. [12]

Fallbacks exist:

- `FFX_HALF=0` selects FP32 arithmetic.
- `FFX_SPD_NO_WAVE_OPERATIONS=1` selects shared-memory downsampling instead of subgroup operations.
- The latter is a build-time shader choice, not automatic selection by AMD's normal host code. Both fallback configurations compiled successfully here. [13]

A concrete portability defect to investigate: the `v1.1.4` GLSL declaration for `rw_luma_history` uses `rgba8`, while AMD's host allocates the luminance-history images as `R16G16B16A16_FLOAT`. The emitted SPIR-V declares `Rgba8`. This is a static discrepancy, not a reproduced MoltenVK failure. It must not be accepted merely because one driver renders something. [9,11]

No current MoltenVK 1.4.2 driver blocker was confirmed. End-to-end correctness remains unconfirmed.

## 4. Inputs and dynamic resolution

| Input | Required convention |
|---|---|
| Color | Jittered scene color, normally linear HDR, before tone mapping and UI |
| Motion vectors | Two floating-point channels, preferably `RG16F`, from the current pixel to its previous-frame location |
| Motion-vector scale | Pixel displacement uses `(1,1)`. Normalized UV displacement uses the motion-vector image's width and height |
| Depth | Current device depth. Reversed depth and an infinite far plane have separate initialization flags |
| Exposure | Optional 1×1 `R32_FLOAT`, or enable internal automatic exposure |
| Reactive mask | Optional render-sized `R8_UNORM`. Values from 0 to 1 reduce reliance on history |
| Transparency/composition mask | Optional render-sized `R8_UNORM`. It limits history locking and color clamping |
| Frame/camera values | Frame duration in milliseconds, near/far distances, vertical field of view in radians, view-unit conversion and positive pre-exposure |

These conventions come from AMD's input documentation and dispatch interface. The masks are optional, but particles and blended surfaces benefit from explicit reactivity. The two masks have different effects and must not become interchangeable names. [10,14]

Tertius must add camera and object motion, including animated geometry. Prefer motion vectors without camera jitter. If vectors include jitter, enable AMD's jitter-cancellation configuration. [10,14]

Use AMD's centered Halton jitter sequence and phase-count helper. Its phase count depends on render width and output width, not a fixed quality label. The implementation returns 8 phases at native resolution, 18 at a 1.5× width ratio and 32 at 2×. Reset history for camera cuts, not ordinary dynamic-resolution changes. [9]

At context creation, provide maximum render and upscale dimensions. At each dispatch, provide the current active dimensions and enable dynamic resolution. AMD retains previous dimensions and allocates internal resources against the maxima. Changing active render size within those limits does not require recreating the context or its internal images. [9,10]

The engine still owns its input-image allocation policy. Maximum-sized attachments with a changing active viewport avoid per-frame allocation. Exceeding a context maximum requires recreating the context and affected resources.

## 5. Frame generation

Frame generation adds optical flow, interpolation and presentation scheduling. AMD supplies a replacement/proxy swapchain and manages generated-frame pacing, synchronization and UI composition. [15]

The older Vulkan presentation implementation uses Windows threads, events, critical sections and performance-counter calls. The macOS port explicitly leaves this part out. Linux integration requires a presentation port. MoltenVK feasibility is unconfirmed. Upscaling does not require frame generation. Keep frame generation outside this integration. [8,15]

## 6. Recommendation and first spike

Recommendation: an upscaler-only integration with engine-owned Vulkan resources and synchronization. Use a pinned AMD shader version and preserve AMD's host calculations rather than rewriting their mathematics from descriptions.

The lowest compiler-risk starting point is `v1.1.4` GLSL, with an explicit format audit. Compare the later `3.1.5` sharpening fix and `3.1.6` precision/reprojection changes before selecting the production revision. This is a recommendation, not a claim that `3.1.4` is preferable indefinitely. [1,3]

A short spike must establish:

1. Actual compute execution on MoltenVK and Linux lavapipe, with validation enabled and matching image formats.
2. Correct stationary, camera-motion, object-motion and disocclusion results. Disocclusion means a surface becomes visible after being hidden.
3. Stable history during repeated render-size changes, exposure changes and camera cuts, without reallocating inside the configured maxima.
4. The same portable baseline on NVIDIA, RADV and ANV before accepting optional FP16 or subgroup optimizations.

Start with FP32 and the no-subgroup variant. Add faster variants only after comparison. The main risks are motion-vector correctness, history bookkeeping, resource-format mismatches and host-port maintenance. FSR 1's render/output size split is reusable groundwork, but its spatial filter supplies none of that temporal state.

## Sources

1. [SDK 2.4.0 release](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/releases/tag/v2.4.0)
2. [GLSL sources](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/tree/v1.1.4/sdk/src/backends/vk/shaders/fsr3upscaler) and [glslang build configuration](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/sdk/src/backends/vk/CMakeShadersFSR3Upscaler.txt)
3. [SDK 2.0.0 release](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/releases/tag/v2.0.0) and [current FSR 3 sources](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/tree/v2.4.0/Kits/FidelityFX/upscalers/fsr3)
4. [Current license and MIT exceptions](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v2.4.0/Kits/FidelityFX/docs/license.md)
5. [FFX API build](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/ffx-api/CMakeLists.txt)
6. [Godot FSR 2 backend](https://github.com/godotengine/godot/blob/master/servers/rendering/renderer_rd/effects/fsr2.cpp)
7. [Linux integration report](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/issues/105)
8. [Native macOS port and patch](https://github.com/SpaceCowboyServer/fsr3-native-macos)
9. [AMD upscaler host implementation](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/sdk/src/components/fsr3upscaler/ffx_fsr3upscaler.cpp)
10. [Public upscaler interface](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/sdk/include/FidelityFX/host/ffx_fsr3upscaler.h)
11. [Private state](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/sdk/src/components/fsr3upscaler/ffx_fsr3upscaler_private.h) and [GLSL declarations](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/sdk/include/FidelityFX/gpu/fsr3upscaler/ffx_fsr3upscaler_callbacks_glsl.h)
12. [Lavapipe device features](https://gitlab.freedesktop.org/mesa/mesa/-/blob/mesa-26.2.3/src/gallium/frontends/lavapipe/lvp_device.c) and [CPU-dependent FP16 support](https://gitlab.freedesktop.org/mesa/mesa/-/blob/mesa-26.2.3/src/gallium/auxiliary/gallivm/lp_bld_limits.h)
13. [Downsampling fallback implementation](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/sdk/include/FidelityFX/gpu/spd/ffx_spd.h)
14. [AMD upscaler integration documentation](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/docs/techniques/super-resolution-upscaler.md)
15. [Frame-generation integration](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/docs/techniques/super-resolution-interpolation.md) and [Vulkan presentation source](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/sdk/src/backends/vk/FrameInterpolationSwapchain/FrameInterpolationSwapchainVK.cpp)
