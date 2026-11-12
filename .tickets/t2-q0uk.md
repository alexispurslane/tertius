---
id: t2-q0uk
status: open
deps: [ter-2zga]
links: []
created: 2026-11-12T11:05:42Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# A packaged Mac build carries the patched MoltenVK and names its two patches in the notices

Found during ter-2zga (2026-11-12). The ticket of the patched MoltenVK says that the Mac build ships the driver. The repository has no packaging step for a Mac build today: the dev shell is the only place that names a Vulkan driver, through VK_ICD_FILENAMES. So ter-2zga changes the dev shell only. When a packaging step for the Mac comes, it takes the driver from the flake output packages.aarch64-darwin.moltenvk (native/moltenvk.nix), not from pkgs.moltenvk. A packaged build without it runs correctly, but its deck frame is about 0.9 ms longer. MoltenVK has the Apache 2.0 license, and section 4(b) asks that changed files carry a notice of the change. So THIRD-PARTY-NOTICES.md then lists MoltenVK with its license and names the two patches (native/moltenvk-execution-dependencies.patch from upstream pull request 2819, and native/moltenvk-keep-dispatch-encoder.patch, ours). The file does not list MoltenVK at all yet. This ticket has no work until a packaging step exists. If the user declines the merge of ter-2zga, close this ticket.

## Acceptance Criteria

A packaged Mac build loads the patched MoltenVK (a trace of the deck shows 79 encoders, not 117), and THIRD-PARTY-NOTICES.md names MoltenVK, its license and the two patches.

