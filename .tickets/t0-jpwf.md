---
id: t0-jpwf
status: open
deps: [t0-5b5b]
links: []
created: 2026-11-12T13:56:24Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Give the reflections a retained deviation and evidence of a drop

Design: docs/plans/bounce-light-confidence.md, section 22. Branch t7-3e22-retained-deviation. Step 5.


## Notes

**2026-11-12T18:07:40Z**

2026-11-12, step 5 built. The reflections keep their retained deviation in an R16F image pair by parity (TemporalSignal.DeviationImage, signal set bindings 14 and 15, only with a precise history; accumulate_temporal.comp's retainedDeviationAt and storeRetainedDeviation), with the same update as the bounce light (step from F, window 32). HistoryConfidence gains DropEvidence(ChangeShare); SignalParameters.Specular holds DropEvidence(1/20); AccumulatePushConstants.Of gives it retainedSigmas 0, so the window test's existing one-sided rule (sumD < 0) applies, and evidence only cuts L to the fast cap (the box stays narrow). TemporalSignal takes NarrowBox for DropEvidence beside the original history and refuses WidenedBox for the reflections and DropEvidence for the diffuse light. Signal set storage images: 10 + age + instrumentation + 2 with a precise history (at most 14); the accumulator's compute stage sees 13 by default, the tracer's 20 stays the most.
Change against the design (written into sections 6 and 12): the pair exists for every reflection signal beside a precise history, not only under DropEvidence. A reflection signal with NarrowBox (test controls only) bound its RGBA16F sample at the pair's R16F bindings, which the precise shader uses statically, and the validation layers reported Undefined-Value-StorageImage-FormatMismatch-ImageView.
Tests, red first: AReflectionWhoseLightDrainsWithoutALightAgeIsCutByItsDrop (theory over uncapped, capped, light-age-diagnostic, control-trace; detail +-50 % at 3 px, CV 0.1, drain to 0.3 with tau 15 after 100 still draws; red 1.211 and 1.170 capped, green 1.042 at 60 draws, bound 1 + 1/20; CPU model of the fixture 1.206 and 1.045), TheReflectionsKeepTheirRetainedDeviationInAnImageOfTheirOwn (red: no image), ASignalRefusesConfidenceRulesThatKeepItsDeviationWhereItsLightKeepsNone (red with the guard removed), DescriptorLayoutsTests.APreciseHistoryAddsFiveStorageImagesToTheScreenTracerAndThreeToTheAccumulator (red 11 against 13). Guards, green before and killed after: ARisingReflectedLightIsNoBlockEvidence, RareBrightReflectionsForFourHundredDraws (RareBrightReflectionSamplesInAStillAreNoEvidenceOfADrop, AStillMirrorUnderRareBrightSamplesKeepsItsSpeckCount against a NarrowBox control), TheOriginalSpecularHistoryKeepsTheNarrowBoxWithoutARetainedDeviation, and ScreenTraceRenderTests+StillPanelForFourHundredDraws now counts the reflections too: 0 reflection cuts in 624,288 block-draws (bounce 6).
Hand mutants: two-sided test killed by the rise (227 cut blocks), the rare still (2,956), the mirror (41,679 specks against 3,477) and the real-ray panel (54,785 reflection cuts); sigma_r read as 0 killed by the rare still (676), the mirror (21,449) and the deviation test; ChangeShare 1/8 killed by every drain row (1.101); the light guard removed killed by the refusal theory. NeighborLimiterPrototype's helper now takes the narrow box, whose rules fit either light.
Suites: Checked and Release all green (Rendering 3119 and 3118 passed, 10 skipped). ARCHITECTURE.md: no count statement changed (the most per stage stays the tracer's 20, 21 under the diagnostic); its prose 'The reflections keep the narrow box (NarrowBox)' and 'No second ... history image is added' are left for step 9. Open for step 7: T3 measures the reflections' false evidence and D's measured over predicted deviation with real rays; the fixture's drain lands at 4.2 %, close to the 1/20 bound.
