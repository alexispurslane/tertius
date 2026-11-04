---
id: ter-cikj
status: open
deps: [ter-gmv8]
links: [ter-wwu7, ter-dgen, ter-gyfe, ter-byim]
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
- Timing, 9 rounds for MoltenVK and the byte-copy control and 8 for KosmicKrisp, at a load of 3 to 32: KosmicKrisp's span is about 2.2 ms (19 %) longer on the deck, even on the floor and the 64-lamp hall, and 0.5 ms longer in the 16-lamp hall, within the control's spread. FSR: 2.73 to 3.00 ms on MoltenVK, 2.91 to 2.99 ms on KosmicKrisp, which can err either way there (ter-dgen).
- Recommendation: keep MoltenVK as the default and KosmicKrisp beside it. Switching is the user's decision.
- After the fix, on KosmicKrisp: Checked 10,180 tests, 2 failed (the span test and the macOS test), 15 skipped; Release 10,167, 3 failed (those two and the BGRA test), 15 skipped. The BGRA test fails in most runs, not all, because ter-gyfe's fault varies.

**2026-11-04T12:32:42Z**

2026-11-04: the settled fork of the macOS test, and the suites after it.
- The orchestrator chose to key the test to the driver. GpuTimingTests.OnMacOsWhereVulkanRunsOnMoltenVkTheRendererSaysThatItsStampsLatchLate is now TheRendererSaysThatItsStampsLatchLateOnMoltenVkAndOnNoOtherDriver. It reads the driver id from a device of its own and expects VulkanRenderer.StampsLatchLate exactly on MoltenVK. StampsLatchLate does not change: it stays keyed to MoltenVK's driver id, because KosmicKrisp's stamps read early, not late (ter-dgen).
- The old test was red on KosmicKrisp. The new test is green on both drivers. A mutant that keys the flag to OperatingSystem.IsMacOS() fails it on KosmicKrisp (tools/mutate.py, killed in 13 s). On MoltenVK that mutant gives the same value, so only KosmicKrisp kills it.
- KosmicKrisp: Checked 10,180 tests, 2 failed, 15 skipped. Release 10,167 tests, 2 failed, 15 skipped. In both, the failures are the span test (ter-dgen) and the BGRA test (ter-gyfe).
- MoltenVK: Checked 10,180 tests, 0 failed, 16 skipped. Release 10,167 tests, 0 failed, 16 skipped.
- The default driver stays MoltenVK in flake.nix. The user decides that separately.

**2026-11-04T13:07:48Z**

2026-11-04: the fixes of the review.
- ter-gyfe: the frame is cut down to five passes, and the mechanism of 2026-11-03 was wrong. With every option that removes a pass off, and every pass skipped but the G-buffer, the depth pyramid, the radiance pyramid, the reflection trace and the reflections, the stores of whole own-ray workgroups still go missing on KosmicKrisp. A marker after the trace shows a workgroup count of 8 in gl_NumWorkGroups in every invocation that stores, also where only 1 or 3 workgroups stored, so the dispatch reads the header correctly. A marker without the trace reaches every listed pixel. Robust buffer and image access do not remove the fault, and the trace's one loop has an integer bound. So the sort as a KosmicKrisp defect stands, with this evidence. ter-gyfe lists the probes. The standalone program still has no trace, so it does not reproduce the fault. Giving it the own-ray shader and the inputs of the cut-down frame is the next step of ter-gyfe.
- The pool rule on MoltenVK: the test's loop reaches maxSets on both drivers, 61 prefilter sets each, so the check runs on MoltenVK too. MoltenVK refuses a set of its own below the loop's limit of 100 (the test passed there before the fix), so a mutant that removes the check survives on MoltenVK. Only a run on KosmicKrisp kills it, and the hardening's mutant table can record it so.
- The per-type half of the pool rule moves to ter-byim. Both drivers gave more prefilter sets than the shared pool's size of samplers holds, so a miscounted type size passes on both Mac drivers.
- Docs: the span on KosmicKrisp and FSR's figure there can err either way, not only low. The recommendation's deck figure is approximate. The span table has 9 rounds for MoltenVK and the control and 8 for KosmicKrisp. The empty-pass test of GpuTimingTests passes on KosmicKrisp without a check of its claim. ARCHITECTURE.md's Descriptor pools section now follows the allocator's last paragraph.
- NoReflections and TraceResolution.Full again gave identical frames on KosmicKrisp, in 7 of 7 pairs each. The 2-pixel difference of the first run's scratch variant 9 has no saved mapping to a setting, so this rerun replaces it.

**2026-11-04T13:37:55Z**

2026-11-04: the hardening.
- Coverage on the changed lines of src/ against main, Rendering tests in Debug and Release: 14 of 14 lines and 2 of 2 branches covered. The changed lines of src/ are all in DescriptorLayouts.cs. The branch changes no C# in Core, Simulation or Physics, so no Stryker run applies. The flake.nix lines are configuration with no mutable code: a wrong ICD path fails every test of a KosmicKrisp run.
- Mutation testing with tools/mutate.py, on MoltenVK (the default) and, for the driver-keyed and refusal mutants, on KosmicKrisp (VK_ICD_FILENAMES=$TERTIUS_KOSMICKRISP_ICD). One row per mutant, with the result over both drivers. The change column names the result of each driver where they differ. The first C05 and C08 runs listed only EnvironmentMapPlumbingTests and DescriptorLayoutsTests, which make no full renderer, so they were rerun with GpuTimingTests and DeviceTests. The first C09 (the field set to null!) failed the style analyzer and was replaced by a mutant that compiles.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| C01 | src/Tertius.Rendering/VulkanRenderer.cs:216 | StampsLatchLate keyed to OperatingSystem.IsMacOS() [MoltenVK: survived, KosmicKrisp: killed] | killed | GpuTimingTests.TheRendererSaysThatItsStampsLatchLateOnMoltenVkAndOnNoOtherDriver | 13.2 |
| C02 | src/Tertius.Rendering/VulkanRenderer.cs:216 | StampsLatchLate == -> != | killed | GpuTimingTests.TheRendererSaysThatItsStampsLatchLateOnMoltenVkAndOnNoOtherDriver | 8.3 |
| C03 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:1340 | remove the refusal at MaxSets [MoltenVK: survived, KosmicKrisp: killed] | killed | EnvironmentMapPlumbingTests.AMapThatCannotGetItsDescriptorSetsReleasesTheImagesAndBuffersItMade | 4.5 |
| C04 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:1340 | refusal == -> > (one set past maxSets) | survived |  | 4.8 |
| C05 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:1340 | refusal one set early | killed | GpuTimingTests.TheRendererSaysThatItsStampsLatchLateOnMoltenVkAndOnNoOtherDriver, GpuTimingTests.NoFrameHasGpuTimingsBeforeTheFirstFrameSlotIsReused, GpuTimingTests.WithVisibilityBitmaskLightTheFourOcclusionPassesRunBetweenTheDirectAndTheAmbientPass | 4.5 |
| C06 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:1351 | remove Given++ [MoltenVK: survived, KosmicKrisp: killed] | killed | EnvironmentMapPlumbingTests.AMapThatCannotGetItsDescriptorSetsReleasesTheImagesAndBuffersItMade | 4.5 |
| C07 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:1329 | pool records maxSets + 1 | survived |  | 4.6 |
| C08 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:838 | shared pool maxSets one short | survived |  | 9.6 |
| C09 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:123 | a bogus pool handle before the constructor makes one | killed | DescriptorLayoutsTests.ALayoutThatNeedsMoreSampledImagesInAStageThanTheDeviceAllowsIsRefusedBeforeItIsMade | 4.4 |
9 rows: killed 6, timeout 0, survived 3, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

- C01, C03 and C06 survive on MoltenVK and die on KosmicKrisp. Judgment call, as the orchestrator settled for C03: MoltenVK refuses the 62nd set and keys the same flag value as macOS, so no MoltenVK test can tell our refusal or our driver key from the driver's own behavior. The KosmicKrisp suite guards both rules, and no machinery is added to make them testable on MoltenVK.
- C04 and C07 (one set past maxSets) survive on both drivers. Judgment call: the plumbing test fills the pool until any refusal, so it passes with a limit one higher. A test that pins the exact count of sets that fit needs the pool's private maxSets, and ter-byim's per-type refusal moves that point for the shared pool. ter-byim's ledger makes every driver refuse at one point, so its tests can pin the count.
- C08 survives on MoltenVK. Judgment call: the shared pool's count holds the sets of the largest pyramids (a 16,384 px side) and the largest environment map. The test renderers are far smaller and never reach it, and a test at 16,384 px costs more than this one count is worth. The line is unchanged by this branch.
- No new tests: every survivor has a judgment call.
- Decision 2 of the orchestrator: the standalone reproduction of ter-gyfe stays in ter-gyfe, and this ticket closes without it. The user declined the Mesa bug reports for now (2026-11-04), and a standalone reproduction serves only such a report.
