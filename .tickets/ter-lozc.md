---
id: ter-lozc
status: in_progress
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

**2026-10-25T18:24:29Z**

User, 2026-10-25: the puffs' additive sum stays finite by a shared budget (design A). Every writer except the puffs caps at 32,256; each puff caps at 32,256 / N for the N puffs drawn this frame; so every pixel stays at or below 64,512. Design B, a 32-bit float image for puffs, was rejected: 32 MB at 1080p, one more pass, and an optional Vulkan feature.

**2026-10-25T18:45:08Z**

2026-10-25: the settle agent built the shared budget (a2f6bfcb) and found that the 992 of room below 65,504 does not take the rounding of many puff blends on a GPU that rounds to nearest (infinity at 106 puffs fully overlapping at their share, by simulation). The orchestrator settled it by the Linux rule (Metal's rounding toward zero hides it) and the user's preference for strong highlights: the puffs' budget becomes 16,128, half the surfaces' 32,256. A blend that rounds to nearest or toward zero makes S + x at most S + 2x, so every pixel stays at or below 64,512 for any N. Cost: in a frame that draws many puffs, each bright puff dims to half the share of the first shared budget.
