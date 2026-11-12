---
id: ter-2zga
status: open
deps: []
links: [te-r7nt]
created: 2026-11-12T07:03:49Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# MoltenVK with the encoder patch, built in the flake for the Mac: 0.93 ms on the deck

Sweep addendum (/private/tmp/tertius-sweep-20261111/addendum.md) A5 and A6 proposal 1. MoltenVK 1.4.2 ends the Metal encoder at every vkCmdPipelineBarrier2 outside a render pass; the patch mvk-keep-dispatch-encoder.patch (form 'keep') keeps an open compute encoder across a barrier whose destination is compute alone, ordering with Metal fences (the three rules of A5). Measured in take 3: deck frame span 24.00 -> 23.07 ms, encoders 116 -> 79, idle time 2.25 -> 1.41 ms, images byte-identical, Metal API validation clean. flake.nix builds pkgs.moltenvk with the patch (measurement switches dropped except the stock-behavior switch; also take upstream's fix for MoltenVK issue 2805), the dev shell's VK_ICD_FILENAMES names it, and the Mac build ships it. The engine must not depend on it: Linux and stock MoltenVK render the same frame. Acceptance: full GPU test suite with the patched driver, once with MTL_DEBUG_LAYER=1, and a deck trace matching A5. Risk: we maintain a driver patch; a wrong fence shows as a rare flicker. THE MERGE WAITS FOR THE USER'S DECISION (2026-11-12 morning); building on a branch is fine.

