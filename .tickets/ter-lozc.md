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

