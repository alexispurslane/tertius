---
id: ter-lozc
status: open
deps: [ter-zzyr, ter-mgze]
links: []
created: 2026-10-25T15:27:48Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, linux]
---
# Make the shaders' NaN and half-float handling portable

The Vulkan spec leaves some results undefined that Metal defines, and the code relies on Metal's answer in places (CLAUDE.md, the Linux rule). SPIR-V FClamp, FMin and FMax give an undefined result when an operand is NaN, and Metal returns a number. A blend into R16G16B16A16_SFLOAT stops at 65,504 on Metal and gives infinity on other GPUs.

## Design

The rule (settled 2026-10-25 from the Vulkan spec): keep NaN and infinity from arising, at their sources; never detect them in a shader. A guard such as isnan(x), or a clamp that turns a NaN into 0, does not work on every driver.

1. Bloom's first pass lost its NaN guard in review. Keep NaN and infinity out of the HDR image at its writers, so that the prefilter needs no guard.
2. surfaceRadiance caps radiance with min(). Keep its operands finite, with the cap before the exposure product, so that the min never sees a NaN or an infinity.
3. Find every other shader operation that can see a NaN, an infinity, a zero or an out-of-domain operand, and every pass that writes or blends into a half-float image. List each site with its verdict in ARCHITECTURE.md, "Portable Vulkan", and fix each one that relies on Metal: a guarded operand at each division, sqrt, log, pow, inversesqrt, normalize, acos and asin, every half-float value and blend sum at or below 65,504, and the CPU inputs read into their ranges once.
4. List the device features and extensions that the renderer requires in ARCHITECTURE.md, and make sure that NVIDIA, RADV, ANV and lavapipe offer each one.

## Acceptance Criteria

No shader relies on a result that the Vulkan spec leaves undefined. Each fixed site has a GPU or CPU test that feeds the input that would break it, except the sites that ARCHITECTURE.md, "Portable Vulkan", names as untestable here with the reason (a fragment exactly at the eye, an exact half in floats, a puff fade's underflow, a ratio that a division rounds past 1, which Metal hides). A test of a fault that Metal hides asserts the cap or reads the intermediate value. ARCHITECTURE.md lists the required device features, extensions and formats.


## Notes

**2026-10-25T15:53:10Z**

From the exposure branch (2026-10-25): pre-exposure's clamp, min(radiance * exposureScale(), vec3(MaxRadiance)), turns a NaN light into 65,504 on MoltenVK, so a NaN glow puff shows white where main showed black. GLSL leaves min undefined for a NaN. Surfaces were already clamped this way on main. Include this site.

**2026-10-25T18:04:27Z**

Builder (2026-10-25). The rule: keep NaN and infinity from arising at their sources, never detect them in a shader. ARCHITECTURE.md, "Portable Vulkan", lists every site with its verdict, the CPU boundary (ShaderInput) and the required device features. Fixed: the zero-normal and at-the-eye normalizations (normalizedOr, towardEye), the specular dominant direction, the cap before the exposure product (exposed), MaxRadiance lowered to 64,512 for the rounding room of the glass blend and the summed bloom levels, the bloom prefilter's clamp removed, the puff fades, impostor.frag's divide by w, the samplerAnisotropy check. Safe with reasons: the meter, the adapt step, the eclipse, the attenuation, the BRDF terms, 1/E1, the shadows, the octahedral code, the outlines, the tone curves, SMAA. Open: the puffs' additive blend, which no cap on one puff bounds; the Blue Danube film peaks at 1,178 in the HDR image with puffs alive, more than 50 times below the cap.

**2026-10-25T18:38:36Z**

Puffs share a budget (user's design A, 2026-10-25). MaxRadiance is now 32,256: every writer of the HDR image but the puffs, and the emissive target and the atlas, cap there. Each puff caps at 32,256 / max(N, 1), N the puffs drawn this frame (ShaderInput.PuffRadianceCap, frame params.x). Exact sums stay at or below 64,512 (ARCHITECTURE.md, Portable Vulkan, the bound). Open: the rounding of many puff blends. A blend that rounds to nearest can add up to 16 per puff; a simulation finds infinity at 106 fully overlapping puffs at their share over a surface at the cap. Metal here rounds toward zero, so no test here shows it. A puff budget of 16,128 would bound the rounded sum for every N.

**2026-10-25T18:51:45Z**

The puffs' budget takes the rounding of their blends (2026-10-25, settled by the Linux rule and the user's preference for strong highlights). ShaderInput.PuffBudget is 16,128, half of MaxRadiance; each puff caps at 16,128 / max(N, 1). A blend that rounds to nearest or toward zero turns S + x into at most S + 2x, so every pixel stays at or below 32,256 + 2 · 16,128 = 64,512 for any N. A blend that rounds toward +infinity is not covered. A CPU test sums Half values for 1 to 4,096 puffs; under the old budget it gave infinity at 106 puffs.

**2026-10-25T19:36:38Z**

Review fixes (2026-10-25). Fixed: the pane's emission capped before the exposure (exposed), the asin of the far shadows capped at 1 in the shader and its C# twin, the outline rim's sqrt floored at 0, light ranges read at most 2^63 and every drawn point within 2^62 of the eye (ShaderInput.InPositionRange), the field of view floored at 1e-6 rad, the trim shares limited to 0.99 in sum, descriptorIndexing checked, GgxLobe's dominant direction falls back to the normal. ARCHITECTURE.md lists the extensions too. Design and Acceptance amended to the rule. No test here can fail for the asin, the rim or the pane's emission, which Metal hides.
