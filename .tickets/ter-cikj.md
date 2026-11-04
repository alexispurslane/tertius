---
id: ter-cikj
status: open
deps: [ter-gmv8]
links: [ter-wwu7, ter-dgen, ter-gyfe]
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

**2026-11-04T02:43:31Z**

2026-11-03: the work of this ticket, reported in docs/research/kosmickrisp.md.
- Setup: the dev shell adds Mesa 26.2.4 on the Mac and sets TERTIUS_KOSMICKRISP_ICD to KosmicKrisp's ICD manifest. MoltenVK stays the default. tools/README.md, "Vulkan drivers", says how to pick KosmicKrisp.
- Before the change, both suites on KosmicKrisp failed the same 4 tests: Checked 10,180 tests, 4 failed, 15 skipped; Release 10,167, 4 failed, 15 skipped. No snapshot test failed.
- Ours: EnvironmentMapPlumbingTests.AMapThatCannotGetItsDescriptorSetsReleasesTheImagesAndBuffersItMade relied on a pool refusal that the spec leaves to the driver ("may fail"). DescriptorLayouts now refuses a set past a pool's maxSets itself (ARCHITECTURE.md, "Descriptor pools"). Red on KosmicKrisp before, green on both drivers after.
- KosmicKrisp: its timestamps (ter-dgen, minimal reproduction in docs/research/kosmickrisp/timestamps.c). This is the cause of ter-wwu7, which can close with this ticket. ScriptCommandCaptureTests' GPU span test stays.
- KosmicKrisp: the own-ray dispatch of the half-resolution reflections runs a varying share of its workgroups (ter-gyfe, reproduction in the engine only). CaptureSinkTests.ABgraOutputHandsItsSinkThePixelsOfAnRgbaOutputInRgbaOrder stays.
- Fork: GpuTimingTests.OnMacOsWhereVulkanRunsOnMoltenVkTheRendererSaysThatItsStampsLatchLate takes macOS for MoltenVK. It stays as it is until the user chooses.
- Timing, 8 rounds per arm with a byte-copy control, at a load of 3 to 32: KosmicKrisp's span is about 2.2 ms (19 %) longer on the deck, even on the floor and the 64-lamp hall, and 0.5 ms longer in the 16-lamp hall, within the control's spread. FSR: 2.73 to 3.00 ms on MoltenVK, 2.91 to 2.99 ms on KosmicKrisp, a lower bound there.
- Recommendation: keep MoltenVK as the default and KosmicKrisp beside it. Switching is the user's decision.
- After the fix, on KosmicKrisp: Checked 10,180 tests, 2 failed (the span test and the macOS test), 15 skipped; Release 10,167, 3 failed (those two and the BGRA test), 15 skipped. The BGRA test fails in most runs, not all, because ter-gyfe's fault varies.

**2026-11-04T12:32:42Z**

2026-11-04: the settled fork of the macOS test, and the suites after it.
- The orchestrator chose to key the test to the driver. GpuTimingTests.OnMacOsWhereVulkanRunsOnMoltenVkTheRendererSaysThatItsStampsLatchLate is now TheRendererSaysThatItsStampsLatchLateOnMoltenVkAndOnNoOtherDriver. It reads the driver id from a device of its own and expects VulkanRenderer.StampsLatchLate exactly on MoltenVK. StampsLatchLate does not change: it stays keyed to MoltenVK's driver id, because KosmicKrisp's stamps read early, not late (ter-dgen).
- The old test was red on KosmicKrisp. The new test is green on both drivers. A mutant that keys the flag to OperatingSystem.IsMacOS() fails it on KosmicKrisp (tools/mutate.py, killed in 13 s). On MoltenVK that mutant gives the same value, so only KosmicKrisp kills it.
- KosmicKrisp: Checked 10,180 tests, 2 failed, 15 skipped. Release 10,167 tests, 2 failed, 15 skipped. In both, the failures are the span test (ter-dgen) and the BGRA test (ter-gyfe).
- MoltenVK: Checked 10,180 tests, 0 failed, 16 skipped. Release 10,167 tests, 0 failed, 16 skipped.
- The default driver stays MoltenVK in flake.nix. The user decides that separately.
