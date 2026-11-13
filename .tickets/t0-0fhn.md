---
id: t0-0fhn
status: open
deps: [t0-jdaq]
links: []
created: 2026-11-13T01:01:07Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Act on the rays' evidence in the accumulation, and remove the block test, the own-sample mean and the reflections' deviation images

Design: docs/plans/bounce-light-confidence.md (Design A with Design X's evidence), section 23. Branch t7-3e22-retained-deviation. Step 12, sections 8.2 to 8.6, 13, 18 and 20 (accumulator). The spatial dispatch reads its pixel's sample age (signal binding 11, new light-age variant of accumulate_spatial.comp), forms the block's evidence level by a shared atomicMax between two barriers in uniform control flow, and acts: light evidence narrows the box and cuts L to FastCap, motion evidence narrows the box only. The temporal dispatch masks the motion bit out of the light code. HistoryConfidence keeps WidenedBox(RetainedSigmas) and NarrowBox; WidenedBox needs a light age. Removes accumulate_blocks.glsl, the block sums (13), the reflections' sigma_r pair (14, 15), the own-sample mean (16, 17), DropEvidence, ChangeShare, KeepsRetainedDeviation, the push constants changeShare and blockTest, and the block test's tests. Files in section 23.

