---
id: tiunr-i1ho
status: closed
deps: []
links: []
created: 2026-11-08T19:49:01Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-icn3
---
# Name the shader variants from a table, not from file-name prefixes

Review finding Q3 of 2026-11-08. TemporalSignal.HistoryShader builds variant file names from strings, with a StartsWith("screen_trace_") rule. A new screen_trace_* kernel without a _lightagediag_precise variant fails only at run time, and only under the diagnostic. A table of the variants that the csproj compiles, checked by a test against the embedded resources, would fail at test time instead.


## Notes

**2026-11-08T22:04:48Z**

Done on branch tiunr-i1ho-shader-variant-table. HistoryInstrumentationSelection holds the table: each instrumentation's ShaderVariant names its file suffix and the shaders that the csproj compiles in it (LightAgeDiagnosticShader, AccumulationTraceShader). TemporalSignal.HistoryShader takes the instrumentation's variant for a shader in its table and the precise variant for every other shader, which keeps a test project's own precise shader working. The table holds no precise list: every shader of a precise history takes the precise variant, and a missing one fails in any render test, because precise-uncapped is the default.

ShaderVariantTests checks the table against the embedded resources both ways. Before the table was filled, the check of embedded variants failed with "found at least one item {accumulate_temporal_lightagediag_precise.comp}", and 7 rows of HistoryShaderTests failed on the expected names. A new screen_trace_* kernel now fails ShaderVariantTests at test time, because the csproj glob embeds its variants and the table lacks it.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| C10 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:73 | the two instrumentations' variants swapped | killed | HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "screen_trace_own_rays.comp", expected: "screen_trace_own_rays_lightagediag_precise.comp"), HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "screen_trace_resolve.comp", expected: "screen_trace_resolve_lightagediag_precise.comp"), HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "screen_trace_block_rays.comp", expected: "screen_trace_block_rays_lightagediag_precise.comp") | 4.8 |
| C01 | src/Tertius.Rendering/Frame/TemporalSignal.cs:189 | original test negated | killed | HistoryShaderTests.TheOriginalHistoryTakesEachShaderAsItIs, HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "screen_trace_own_rays.comp", expected: "screen_trace_own_rays_lightagediag_precise.comp"), HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "screen_trace_resolve.comp", expected: "screen_trace_resolve_lightagediag_precise.comp") | 5.0 |
| C02 | src/Tertius.Rendering/Frame/TemporalSignal.cs:192 | every shader takes the instrumented variant | killed | HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "accumulate_spatial.comp", expected: "accumulate_spatial_precise.comp"), HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "composite.frag", expected: "composite_precise.frag"), HistoryShaderTests.TheTraceTakesItsVariantForBothDispatchesAndThePreciseOneElsewhere(shader: "screen_trace_resolve.comp", expected: "screen_trace_resolve_precise.comp") | 4.6 |
| C03 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:72 | None gets the diagnostic variant | killed | ShaderVariantTests.AnInstrumentationWithoutAnImageHasNoVariant | 4.4 |
| C04 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:73 | diagnostic gets the trace variant | build-failed |  | 2.2 |
| C05 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:74 | trace gets the diagnostic variant | build-failed |  | 2.2 |
| C06 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:36 | suffix after the stage | killed | HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "screen_trace_own_rays.comp", expected: "screen_trace_own_rays_lightagediag_precise.comp"), ShaderVariantTests.EveryShaderOfAnInstrumentationsVariantIsEmbeddedInThatVariantAndAsPrecise, HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "screen_trace_resolve.comp", expected: "screen_trace_resolve_lightagediag_precise.comp") | 4.7 |
| C07 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:32 | precise suffix changed | killed | HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "accumulate_spatial.comp", expected: "accumulate_spatial_precise.comp"), ShaderVariantTests.EveryShaderOfAnInstrumentationsVariantIsEmbeddedInThatVariantAndAsPrecise, HistoryShaderTests.TheDiagnosticTakesItsVariantForTheTemporalDispatchAndTheTracerAndThePreciseOneElsewhere(shader: "composite.frag", expected: "composite_precise.frag") | 8.2 |
| C08 | src/Tertius.Rendering/Frame/HistoryInstrumentation.cs:52 | one kernel missing from the diagnostic table | killed | ShaderVariantTests.EveryEmbeddedInstrumentedVariantIsInItsInstrumentationsTable | 8.4 |
| C09 | src/Tertius.Rendering/Frame/TemporalSignal.cs:176 | instance drops the instrumentation | killed | AccumulationTraceTests.TheAntiLagShortensTheLengthByTheTracedLagOverItsShare, AccumulationTraceTests.AStillUnjitteredPixelReprojectsOntoItsOwnTexelWithItsWholeWeight, AccumulationTraceTests.TheSpatialDispatchStartsFromTheReprojectedLengthPlusTheBlendedFrame | 47.1 |
10 rows: killed 8, timeout 0, survived 0, no-coverage 0, build-failed 2, no-tests 0, no-mutant 0, removed 0

C04 and C05 each left one private variant unread, so IDE0052 failed their builds. C10, which swaps the two arms, replaces them and is killed.

**2026-11-08T22:05:27Z**

Coverage of the changed lines, Debug, the classes ShaderVariantTests, HistoryShaderTests and AccumulationTraceTests: 29/29 lines, 9/10 branches. The partial branch at HistoryInstrumentation.cs:70 is the compiler's arm for an enum value outside HistoryInstrumentation, which no instrumentation reaches, so it needs no test.
