---
id: ter-kzj3
status: open
deps: []
links: []
created: 2026-10-25T15:25:29Z
type: feature
priority: 3
assignee: Alexis Purslane
tags: [platform, linux, rendering]
---
# Run the game and every test on Vulkan on Linux

The user requires that the game runs on regular Vulkan on Linux, not only on MoltenVK on the Mac (user, 2026-10-25). Nothing has run on Linux yet. The flake already lists x86_64-linux and aarch64-linux, and only the Darwin shell adds MoltenVK.

## Design

1. Build the Linux dev shell and run the full Checked and Release suites on Mesa lavapipe (software Vulkan), in an aarch64-linux VM on this Mac or on the user's Linux machine (the user picks).
2. Turn on the Vulkan validation layers in that run, with the synchronization checks, and fix every error.
3. Audit NaN and infinity handling in every shader. SPIR-V FClamp, FMin and FMax with a NaN operand are undefined, and Metal turns NaN into 0. Known sites: the bloom prefilter lost its NaN guard, and surfaceRadiance caps radiance with min(). Make each one explicit and portable, with a GPU test that feeds a NaN or an infinity.
4. Audit half-float overflow: a blend into R16G16B16A16_SFLOAT saturates at 65,504 on Metal and gives infinity on other GPUs. Bloom and glass cap their outputs already. Find every other pass that writes or blends HDR values.
5. List every device feature and extension that the renderer requires, and make sure that NVIDIA, RADV, ANV and lavapipe offer each one.
6. Make sure that the GLFW window opens and presents on Linux, with X11 and with Wayland.

## Acceptance Criteria

The full Checked and Release suites pass on Linux with lavapipe. The validation layers report no errors. The demo runs in a window on Linux. ARCHITECTURE.md lists the supported platforms and the required device features.


## Notes

**2026-10-25T15:27:48Z**

User, 2026-10-25: skip the Linux runs for now. Design with Linux in mind, to the Vulkan spec. So this ticket waits, at priority 3. The shader audit (steps 3 and 4) moved to its own ticket, because it is spec work that needs no Linux machine.
