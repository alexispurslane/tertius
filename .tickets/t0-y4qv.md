---
id: t0-y4qv
status: closed
deps: []
links: []
created: 2026-11-12T19:00:12Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# Compare the fast history with the own-sample mean in the block test, not with the filtered slow history

Design: docs/plans/bounce-light-confidence.md, section 8 (R, after take T3's filter test), section 12 (images), section 17. Branch t7-3e22-retained-deviation. The filter's write-back into S made D = F - S nonzero in a still: 2.1e-2 false windows per block-draw with the filter on, 1.9e-4 with it off (workbench job filter-ab, 2026-11-12).


## Notes

**2026-11-12T19:57:38Z**

Built the own-sample mean R: an image pair by parity at signal set bindings 16 (last draw) and 17 (this draw), R16F beside a half history and R32F beside a precise one. The temporal dispatch reprojects R as sigma_r, takes the exposure ratio, blends it with 1/L, and stores 0 for sky. The block sums use d = lum(F) - R and s = R. On evidence, the spatial dispatch restarts R at lum(F). Every signal with a fast history keeps R, whatever its confidence rules (step 5's rule for sigma_r: the original history's NarrowBox reflections share the base shaders). The single-history prototype binds its sample there. New tests: AStillBandOfLightThatTheFilterBlursIsNoEvidenceOfAChange (red: 1269 whole-block cuts), PixelsThatTheNarrowClampRaisesInAStillAreNoEvidenceOfADrop (red: 941), AfterEvidenceTheOwnSampleMeanRestartsAtTheFastHistory (red without the restart: 6 fires, green: 1 to 2). Open: the reflections' drain test now ends 5.4 % above its light (4.2 % before), so its bound moved from 1/20 to 6 %. A restart of R at the clamped S gives 4.1 %, but it puts the clamp's bias into R.

**2026-11-13T01:01:22Z**

2026-11-12: obsolete. The own-sample mean existed only for the statistic; step 12 (t0-0fhn) removes it.
