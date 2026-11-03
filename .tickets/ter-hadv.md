---
id: ter-hadv
status: open
deps: []
links: []
created: 2026-11-02T14:07:44Z
type: chore
priority: 3
assignee: Alexis Purslane
---
# Remove the two late storage-image checks that DescriptorLayouts now precedes

Since ter-55xu, DescriptorLayouts counts the storage buffers, storage images and sampled images that each stage of each pipeline layout sees, and refuses a device above a maxPerStageDescriptor limit before vkCreatePipelineLayout (LimitedDescriptors.Problem). It makes the depth tail layout (10 storage images) and the occlusion layout (9) at every start, so it refuses a device below 10 storage images before RenderTargets.CheckFormats (DepthPyramidTail.StorageImageProblem) or OcclusionImages.CheckDevice (OcclusionImages.StorageImageProblem) can run. Those two checks can no longer fire. Remove them and their tests (DepthPyramidTailTests, OcclusionImagesTests), keep the constants that size their sets, and update ARCHITECTURE.md, Required device features.


## Notes

**2026-11-03T14:54:04Z**

2026-11-03: ter-gayt, the screen tracer, deletes OcclusionImages. So this ticket shrinks to the depth tail's check (docs/plans/temporal-renderer.md, section 13).
