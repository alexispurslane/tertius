---
id: tiunr-7haj
status: in_progress
deps: [tiunr-4b2g]
links: [tiunr-py6q, tiunr-g9ia, tiunr-okzm]
created: 2026-11-08T03:38:54Z
type: task
priority: 2
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# Make the light-change age the renderer's default

User decision 2026-11-08: after the pixel rule's acceptance (tiunr-4b2g), the light-change age (built under tiunr-py6q, TERTIUS_REFLECTION_HISTORY_AGREEMENT=light-age) becomes the renderer's behavior. Evidence: /private/tmp/tertius-light-age-history-20261107/report.md (20% dimming adjusts in 36 updates, still scenes unchanged, 18 B/px) and /private/tmp/tertius-uncovered-age-20261107/report.md (jitter resets no light age). Settled fork: the light age becomes unconditional, like the pixel rule; the TERTIUS_LIGHT_AGE shader variants and the off value go, and the diagnostic and trace values stay as opt-in instrumentation. The uncovered-surface starting age A stays at no evidence. Then full suites, coverage, mutation table, ARCHITECTURE.md and screenshots. The box fix tiunr-nxtr follows it, so that its measurement runs with the light age on.


## Notes

**2026-11-08T03:39:00Z**

The light-age evidence ran before the pixel rule and without FSR jitter. The acceptance must measure the 20% dimming response and the still rough and smooth scenes again, with FSR and the pixel rule, against the sealed light-age numbers.

**2026-11-08T05:22:32Z**

2026-11-08: the light-change age is the renderer's behavior (uncommitted). Every precise specular history keeps it, capped and uncapped; `original` keeps H13's shaders, which have none, and refuses the instrumentation. `TERTIUS_REFLECTION_HISTORY_AGREEMENT` now selects only instrumentation (`HistoryInstrumentation`: none, `light-age-diagnostic`, `control-trace`); `off` and `light-age` stop the renderer. The `_lightage_precise` modules are gone: the light-age blocks compile under `TERTIUS_PRECISE_SPECULAR_HISTORY`, the trace moved to signal binding 12. FSR measurement, identity check (361 render files byte-identical to the opt-in light age), suites and screenshots: /private/tmp/tertius-light-age-default-20261108/report.md.

Mutants ran through tools/mutate.py in a separate repository copy of the worktree, deleted after the runs. Run 1: C01 to C19 (stopped at C19, see below). Run 2: C20, S01 to S10. Run 3: C20 and S05 after their new tests. Logs: /private/tmp/tertius-light-age-default-20261108/mutants/run1, run2, run3.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| C01 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:39 | empty value no longer selects none | killed | LightAgeHistoryTests.ByDefaultALastingDimming...(value: "") | 20.7 |
| C02 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:40 | diagnostic value selects none | killed | LightAgeHistoryTests.TheDiagnosticSeesTheRaysThatHitAFreshlyUncoveredSurface, TheInstrumentationNeedsAPreciseSpecularHistory(light-age-diagnostic), TheDiagnosticGivesEachReceiverTheFramesSeen... | 20.9 |
| C03 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:41 | trace value selects none | killed | AccumulationTraceTests.UnderTheHeldJitter..., APlateBelowTheHandOver..., TheLagIsTheClampsMove... | 26.5 |
| C04 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:42 | light-age falls to the unknown-value message | killed | LightAgeHistoryTests.TheRemovedSwitchValuesFailBecauseTheLightAgeIsAlwaysOn(light-age) | 19.5 |
| C05 | src/Tertius.Rendering/Frame/SpecularHistoryMode.cs:21 | capped history keeps no light age | killed | DescriptorLayoutsTests.TheCappedPreciseHistoryHasTheSameLayoutsAsTheUncapped, LightAgeHistoryTests.TheCappedPreciseHistoryAlsoShortensToTheLightsAge | 22.4 |
| C06 | src/Tertius.Rendering/Frame/SpecularHistoryMode.cs:21 | original history keeps the light age | killed | DescriptorLayoutsTests.APreciseHistoryAddsTheLightAgesThreeStorageImages..., LightAgeHistoryTests.TheInstrumentationNeedsAPreciseSpecularHistory (2) | 20.8 |
| C07 | src/Tertius.Rendering/Frame/SpecularHistoryMode.cs:34 | empty history value refused | killed | SpecularHistorySelectionTests.EachSettingSelectsItsHistory("", PreciseUncapped) | 4.5 |
| C08 | src/Tertius.Rendering/Frame/SignalScratch.cs:35 | original accepts the instrumentation | killed | (test host exit 139) | 8.3 |
| C09 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:103 | tracer set always has the light-age images | killed | DescriptorLayoutsTests.APreciseHistoryAddsTheLightAgesThreeStorageImages... | 20.7 |
| C10 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:104 | tracer set lacks the diagnostic | killed | DescriptorLayoutsTests.TheUncoveredDiagnosticAddsOneStorageImage... | 5.1 |
| C11 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:119 | signal set always has the sample age | killed | DescriptorLayoutsTests.APreciseHistoryAddsTheLightAgesThreeStorageImages... | 20.3 |
| C12 | src/Tertius.Rendering/Frame/DescriptorLayouts.cs:120 | signal set lacks the trace binding | killed | DescriptorLayoutsTests.TheAccumulationTraceAddsOneStorageImageToTheAccumulatorOnly | 4.8 |
| C13 | src/Tertius.Rendering/Frame/TemporalSignal.cs:185 | tracer kernels skip the diagnostic variant | killed | LightAgeHistoryTests.TheDiagnosticSeesTheRaysThatHitAFreshlyUncoveredSurface, TheDiagnosticGivesEachReceiverTheFramesSeen... | 19.7 |
| C14 | src/Tertius.Rendering/Frame/TemporalSignal.cs:190 | spatial dispatch skips the trace variant | killed | AccumulationTraceTests.TheLagIsTheClampsMove..., TheAntiLagShortensTheLength..., TheTraceGivesTheLengthThatTheSpatialDispatchAccepts | 25.8 |
| C15 | src/Tertius.Rendering/Frame/TemporalSignal.cs:424 | no sample age without the diagnostic | killed | AccumulationTraceTests.UnderTheHeldJitter..., APlateBelowTheHandOver..., TheLagIsTheClampsMove... | 9.3 |
| C16 | src/Tertius.Rendering/Frame/TemporalSignal.cs:490 | trace written at binding 11 | killed | AccumulationTraceTests.UnderTheHeldJitter..., APlateBelowTheHandOver..., TheLagIsTheClampsMove... | 25.9 |
| C17 | src/Tertius.Rendering/Frame/TemporalSignal.cs:479 | capped signal set leaves the sample age unbound | killed | LightAgeHistoryTests.TheCappedPreciseHistoryAlsoShortensToTheLightsAge | 20.3 |
| C18 | src/Tertius.Rendering/Frame/ScreenTraceImages.cs:148 | capped tracer keeps no light-age records | killed | LightAgeHistoryTests.TheCappedPreciseHistoryAlsoShortensToTheLightsAge | 20.1 |
| C19 | src/Tertius.Rendering/VulkanRenderer.cs:168 | layouts made for the original history | killed (tool: no-tests) | test host exit 139 before the first result | 4.9 |
| C20 | src/Tertius.Rendering/Frame/TemporalSignal.cs:106 | diffuse signal takes the instrumentation | killed (run 3; survived run 2) | AccumulationTraceTests.TheDiffuseSignalKeepsNoTrace (new) | 31.3 |
| S01 | src/Tertius.Rendering/Shaders/accumulate.glsl:30 | trace declared at binding 11 | killed | AccumulationTraceTests.UnderTheHeldJitter..., APlateBelowTheHandOver..., TheLagIsTheClampsMove... | 42.1 |
| S02 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:297 | temporal dispatch skips the age cut | killed | LightAgeHistoryTests.ByDefaultALastingDimming...(both), ACutHistoryGrowsBackWithTheAgeOfTheLight | 41.7 |
| S03 | src/Tertius.Rendering/Shaders/screen_trace_plan.comp:99 | plan writes no light-age record | killed | LightAgeHistoryTests.TheDiagnosticSeesTheRays..., ADisoccludedSurfaceHasNoLightAgeEvidence, ByDefaultALastingDimming...("") | 25.7 |
| S04 | src/Tertius.Rendering/Shaders/screen_trace_resolve.comp:67 | resolve leaves a sky or impostor pixel's age unwritten | survived | judgment call, below | 29.3 |
| S05 | src/Tertius.Rendering/Shaders/screen_trace_resolve.comp:100 | resolve ignores the diffuse ray's age | killed (run 3; survived run 2) | LightAgeHistoryTests.ARoughReceiversDiffuseRayBringsTheAgeOfTheDimmedEmitter (new) | 30.4 |
| S06 | src/Tertius.Rendering/Shaders/screen_trace_resolve.comp:115 | resolve ignores the reflection ray's age | killed | LightAgeHistoryTests.ByDefaultALastingDimming...(both), ACutHistoryGrowsBackWithTheAgeOfTheLight | 30.1 |
| S07 | src/Tertius.Rendering/Shaders/screen_trace_resolve.comp:129 | resolve writes no sample age | killed | LightAgeHistoryTests.ByDefaultALastingDimming...(both), ACutHistoryGrowsBackWithTheAgeOfTheLight | 30.3 |
| S08 | src/Tertius.Rendering/Shaders/screen_trace.glsl:407 | miss age left unset | killed | AccumulationTraceTests.TheTraceRendersExactlyAsTheDefaultUnderJitter, TheClampedHistoryLiesInsideTheTracedBox, LightAgeHistoryTests.TheDiagnosticRendersExactlyAsTheDefault | 41.5 |
| S09 | src/Tertius.Rendering/Shaders/screen_trace.glsl:426 | hit brings no age | killed | LightAgeHistoryTests.TheDiagnosticSeesTheRays..., ByDefaultALastingDimming...(both) | 41.4 |
| S10 | src/Tertius.Rendering/Shaders/screen_trace.glsl:468 | record stores no age code | killed | LightAgeHistoryTests.TheDiagnosticSeesTheRays..., ByDefaultALastingDimming...(both) | 41.6 |
30 rows: killed 29, timeout 0, survived 1, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Answers:
- C19: mutate.py reported no-tests, because the test host crashed with exit 139 (SIGSEGV) before the first result: the renderer's layouts lacked the light-age bindings that its shaders use. tools/README.md counts a host crash as killed. C08 crashed the same way and the tool counted it killed.
- C20 (killed by a new test): the trace observes the specular history alone; with the mutant, the diffuse signal also allocated a nine-layer RGBA32F trace, 144 bytes per pixel, that no shader writes.
- S05 (killed by a new test): above the tracer's MaxRoughness of 0.5 the specular sample takes only the diffuse ray, so its age must come from that ray. The new test dims the emitter under a plate of roughness 0.7.
- S04, judgment call: the zero write covers only sky and impostor pixels, where the composite reads no signal and the temporal dispatch returns before it reads the age; a stale age there could only shorten an unread history, and a test would have to turn a pixel that held an age into sky or an impostor.
- TheCappedPreciseHistoryAlsoShortensToTheLightsAge was not red before the change for the right reason (its first bound of 32 frames lay at the capped history's cap, so it found 0 receivers in any build); with the bound at 8 frames, C05, C17 and C18 show it red on a capped history without the light age.
- The capture tool's lines (Program.History.cs and two cref lines) are not in the list: tools/README.md lists Rendering, Host, Demo and the analyzers. The step-5 captures ran through RequireHistoryResources with the new checks.

Coverage (Checked, Rendering tests): 76 of 77 executable added C# lines in src; the one gap, the unknown TERTIUS_SPECULAR_HISTORY value, is now covered by SpecularHistorySelectionTests.AnUnknownSettingIsRefused.

**2026-11-08T05:28:59Z**

2026-11-08: verified the report: checked3 10,325 run 0 failed (00:18), release3 10,312 run 0 failed (00:25); FSR dimming 36 updates on the plate, 92 on the final output; still scenes byte-identical off and on; default equals the opt-in captures in 361 files. Mutants 29 killed, S04 a judgment call. Open: the still reflections scene gets light-age evidence (tiunr-okzm).
