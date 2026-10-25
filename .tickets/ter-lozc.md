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

1. Bloom's first pass lost its NaN guard in review. Add an explicit, portable guard, with a GPU test that feeds a NaN texel and makes sure that no NaN reaches the glow.
2. surfaceRadiance caps radiance with min(). Make the cap correct for a NaN input by the spec, with a GPU test.
3. Find every other shader that uses clamp, min or max on a value that can be NaN or infinity, and every pass that writes or blends into a half-float image. List each site in the ticket with its verdict, and fix each one that relies on Metal.
4. List the device features and extensions that the renderer requires in ARCHITECTURE.md, and make sure that NVIDIA, RADV, ANV and lavapipe offer each one.

## Acceptance Criteria

No shader relies on a result that the Vulkan spec leaves undefined. Each fixed site has a GPU test that feeds a NaN or an infinity. ARCHITECTURE.md lists the required device features.


## Notes

**2026-10-25T15:53:10Z**

From the exposure branch (2026-10-25): pre-exposure's clamp, min(radiance * exposureScale(), vec3(MaxRadiance)), turns a NaN light into 65,504 on MoltenVK, so a NaN glow puff shows white where main showed black. GLSL leaves min undefined for a NaN. Surfaces were already clamped this way on main. Include this site.

**2026-10-25T18:04:27Z**

Builder (2026-10-25). The rule: keep NaN and infinity from arising at their sources, never detect them in a shader. ARCHITECTURE.md, "Portable Vulkan", lists every site with its verdict, the CPU boundary (ShaderInput) and the required device features. Fixed: the zero-normal and at-the-eye normalizations (normalizedOr, towardEye), the specular dominant direction, the cap before the exposure product (exposed), MaxRadiance lowered to 64,512 for the rounding room of the glass blend and the summed bloom levels, the bloom prefilter's clamp removed, the puff fades, impostor.frag's divide by w, the samplerAnisotropy check. Safe with reasons: the meter, the adapt step, the eclipse, the attenuation, the BRDF terms, 1/E1, the shadows, the octahedral code, the outlines, the tone curves, SMAA. Open: the puffs' additive blend, which no cap on one puff bounds; the Blue Danube film peaks at 1,178 in the HDR image with puffs alive, more than 50 times below the cap.
