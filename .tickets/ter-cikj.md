---
id: ter-cikj
status: in_progress
deps: [ter-gmv8]
links: [ter-wwu7]
created: 2026-11-03T23:31:32Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [rendering, portability, performance]
---
# Run Tertius on KosmicKrisp, and switch the dev shell to it if it is correct and not slower

Khronos deprecated MoltenVK (github.com/KhronosGroup/MoltenVK README: 'Khronos has deprecated MoltenVK... For Vulkan on Metal, Khronos now recommends KosmicKrisp on Apple Silicon Macs'). KosmicKrisp is LunarG's Mesa driver, Vulkan 1.4 conformant since 2026-10-01 (Khronos news), translating NIR to MSL without SPIRV-Cross. Khronos says its performance tripled since its 1.3 conformance and that optimization goes on. The pinned nixpkgs builds Mesa 26.2.4 on macOS with the kosmickrisp driver, from the binary cache (7.5 MiB): its ICD is share/vulkan/icd.d/kosmickrisp_mesa_icd.aarch64.json. On this Mac (macOS 27.0) vulkaninfo shows that it offers every feature and limit of ARCHITECTURE.md's 'Required device features', with no portability subset, so it is a full conformant driver and a stronger check of the engine's spec compliance than MoltenVK. Its timestampPeriod is 41.67 ns, against MoltenVK's 1. The demo crashed on it at the first upload (the allocator bug, which this ticket depends on).

Scope: 1. In flake.nix, add Mesa to the Darwin dev shell and an environment variable that names its KosmicKrisp ICD, so that a run picks the driver by VK_ICD_FILENAMES without a store path in scripts. 2. Run the full Checked and Release suites under KosmicKrisp. Sort each failure: a defect of ours against the Vulkan spec (fix it, red test first), a KosmicKrisp defect (a ticket, with a minimal reproduction), or a legitimate difference (for example the snapshot tolerance). 3. Time the two drivers in alternating locked runs on the views that the spikes use (the deck, the floor, the 16- and 64-lamp halls), with a byte-copy control, and FSR's timing test. 4. Report, and recommend: switch the dev shell's default driver to KosmicKrisp, keep MoltenVK, or keep both for comparison. The switch is a fork for the user if the drivers differ in speed or correctness.


## Notes

**2026-11-04T01:19:27Z**

2026-11-03: started as a workflow, run 8d2999f3-bb50-4002-9ba1-cbabdbdd0c91 (version 869b0b07-3413-4b06-8707-a88a91bf6e91), branch ter-cikj-kosmickrisp from 3018644d9. The run also sorts ter-wwu7, the KosmicKrisp timing failure.

**2026-11-04T11:54:12Z**

2026-11-04: the first run (8d2999f3) stopped at two forks. The orchestrator settled the StampsLatchLate test as option (b): key the test to the driver. The default driver waits for the user, and MoltenVK stays the default meanwhile. Continuation run bf033fe9-e9d5-4e63-bb51-2a0e06668abf on the same branch and worktree applies (b), recounts both drivers' suites, and then the review runs.
