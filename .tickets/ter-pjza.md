---
id: ter-pjza
status: open
deps: []
links: []
created: 2026-11-01T02:14:19Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Occlusion passes and ambient light within their share of the 12 ms frame: 2.76 ms to 1.75 ms on demo-exit

Part 3 of docs/plans/rendering-performance.md, split out of ter-3g1g by the orchestrator on 2026-10-31, so that the largest groups of passes are cut in parallel instead of one after another. The measurement (ter-3g1g, note of 2026-10-31): demo-exit at 1920 x 1080, Release, main 87ee9caf, Apple M1 Max, load 2 to 3. Two runs without a trace gave a mean GPU span of 18.04 and 17.94 ms; a trace of every frame without counters gave 17.56 ms. Each pass's share is its own busy time per frame in that trace (the mean over all 3,889 frames), times (12 ms - 1.87 ms of gaps between passes) / 16.03 ms = 0.632.

The screen-space light's passes and the ambient pass take, in ms per frame (own time, then share): occlusion depth 0.122 and 0.077, occlusion sectors 1.578 and 0.997, occlusion denoise 0.107 and 0.067, occlusion upsample 0.310 and 0.196, ambient light 0.647 and 0.409. Together 2.763 ms against a share of 1.746 ms, 1.02 ms less. Part 2a (ter-izsu) profiled the occlusion passes, and ter-fhlb cut the ambient pass's footprint.

## Acceptance Criteria

The five passes' own busy time on demo-exit (a trace of every frame, two runs or a paired trace) is at most 1.75 ms, or the changes that keep the image are exhausted and a note gives the remainder and each candidate default change with its measured saving and captures. Every change keeps the image by the pixel comparison of decision 7. ARCHITECTURE.md gives the new cost.

## Notes

**2026-11-01T03:19:41Z**

Profile before any change (builder, 2026-10-31), step 1. Base ef3b6ae5 (main 87ee9caf plus tickets only), Release, 1920 x 1080, Apple M1 Max, every GPU run under the GPU lock.

Counter traces (tools/gpu-trace.sh with counters, 120 frames; own busy time of each pass in ms, then the top limiters and the read bandwidth):
- deck, --start 399 (load 3.94 at the start): occlusion depth 0.115 (texture cache 77 %, texture sample 77 %, 59 GB/s, compute occupancy 65 %); occlusion sectors 1.365 (ALU 72 %, last-level cache 53 %, 54 GB/s, compute occupancy 18 %); occlusion denoise 0.096 (ALU 40 %, texture filtering 39 %); occlusion upsample 0.267 (ALU 73 %, texture sample 23 %, occupancy 49 %); ambient light 0.573 (ALU 32 %, texture filtering 16 %, fragment occupancy 22 %, 22 GB/s read).
- floor, --start 2412 (load 5.79): depth 0.126, sectors 1.712 (last-level cache 66 %, ALU 64 %, 92 GB/s), denoise 0.110, upsample 0.319, ambient 0.641 (ALU 30 %).
- Whole run, ter-3g1g's trace of every frame (same code): depth 0.122, sectors 1.578, denoise 0.107, upsample 0.310, ambient 0.647.

Limiters. The sector pass is bound by its arithmetic and its last-level cache together, at a low occupancy (18 %): the steps read the depth, the geometric normal, the two depths around and the bounce source at scattered places, and their misses show where the radius spans many texels (floor). The upsample is bound by its arithmetic. The ambient pass is bound by no unit (ALU 30 to 32 %, occupancy 22 %): latency. The depth pass is bound by its strided reads of the full-resolution images (one pixel of each 2 x 2 block), and is small.

Against the design (docs/plans/screen-space-illumination.md, "Cost"): the sectors pass takes 1.37 to 1.71 ms against 0.9 to 2.8 ms expected, the upsample 0.27 to 0.32 against 1.4 to 1.5, the denoise 0.10 to 0.11 against 0.5 to 0.6, the depth pass 0.12 against 0.13, the ambient pass 0.57 to 0.65 against 0.3 to 0.7. Each is within or below its estimate; the group is over its share of the 12 ms frame (2.763 against 1.746 ms), not over its design.

Where the time goes, by probe builds (temporary shader edits, not committed): plain headless runs of the deck window (the trimmed script of the trace, --video none, about 6 s each), the probe build against a saved copy of the base build in 6 rounds of alternating order (A B, B A, ...), the sum of the five passes from the renderer's timer (a sum of neighbouring passes, which holds on MoltenVK), median of the per-round differences. A/A control (two copies of the base): +0.033 ms (range +0.008 to +0.394; the large one a slow run). Loads 1.3 to 8 during the probes.
- the whole step loop of the sector pass: -1.03 ms (no steps). So the setup of each texel and its slices, with its own surface's run, costs about 0.38 ms, and the steps about 1.04 ms.
- inside the steps: the depth read and the loop alone about 0.28 ms; the read and decode of each step's geometric normal 0.21 ms; the two depths around each step's place 0.11 ms; the rest up to the radius test about 0.13 ms; the sector runs and the bounce 0.12 ms; the map's light along the blocked directions 0.10 ms.
- the own surface's run (atan2 and its map light): 0.12 ms.
- the ambient pass's additive write to the diffuse image under the skin scatter (one more attachment loaded and stored over the whole screen, to add zeros everywhere but at skin): 0.076 ms.

Plan, largest first, each measured on its own: (1) the sector steps read one image for the depth and the geometric normal of a texel; (2) the upsample works per 2 x 2 block of pixels, which shares the texels' reads and positions; (3) the ambient pass adds the skin's ambient part only at skin pixels; then the setup of the sector pass.

**2026-11-01T03:48:07Z**

Change 1, the upsample by 2 x 2 blocks (builder, 2026-10-31). One invocation per half-resolution texel writes its block of four pixels, each through the old per-pixel body, so the compiler shares the reads of the block's four texels and their positions (an invocation per pixel read them up to nine times per block). Work: a quarter of the invocations and fewer image reads; no other change of memory traffic, and the workgroup stays 8 x 8 = 64.
- Pixels: every capture byte for byte the base's (largest difference 0 levels, 0 pixels differ): demo-exit one frame per film (captures/pjza/demo-exit-films.json: unbelt, deck, chute-top, chute, brake-run, floor, stack, spinward, antispinward), demo-surface (7), cabin-light (3), mirror-balls (3), bounce-raised-box, bounce-red-wall, at 1920 x 1080, Release. Two base renders agree byte for byte, so the comparison has no noise.
- A first version passed the four texels' positions and terms to each pixel as arrays. It changed 1 level in 0.0005 to 0.03 % of the pixels and 11 levels at one pixel of spinward, (1175, 498) on the pillar's silhouette, where SMAA's edge test turned a sub-level change into a larger one. A version with run-time bilinear weights did the same. So every change here must keep the estimate's bits, not only its look.
- Cost: plain runs of the deck window (120 frames from 399), base build against this build in 6 rounds of alternating order, the renderer's timer: the upsample and the ambient pass together -0.048 ms per frame (median of the per-round differences, range -0.059 to -0.024), the sector pass and its neighbours -0.007 (range -0.038 to +0.030, unchanged), load 2.9 to 5.3.
- Test: OcclusionUpsampleShaderTests.TheUpsampleWritesEveryPixelAsTheCSharpCopyDoes compares every pixel of a 642 x 361 image with OcclusionUpsample (the last row without a row below, and the odd pixels of the last column beside a texel beyond the half-resolution image). It passes on the old shader and the new one; a mutant that writes only the even rows of each block fails it.

**2026-11-01T04:17:54Z**

Change 2, one read per step for its depth and its normal (builder, 2026-11-01). The depth pass's copy of the geometric normal (RG16 SNORM, 4 B per texel) becomes OcclusionImages.DepthAndNormal (RG32UI, 8 B): x the bits of the view depth's float, y the normal as packSnorm2x16 packs it. A near step reads its depth and its normal in one load from it; the edges, the depths around and the upsample keep reading the 4 B view depth image. Memory: +2.07 MB at 1080p (the copy 2.07 to 4.15 MB). Portable: R32G32_UINT storage is a required format of Vulkan; the rg32ui declaration needs shaderStorageImageExtendedFormats, which the renderer already requires. NVIDIA, AMD and Intel GPUs read images through caches by lines or tiles of neighbouring texels, as Apple's does, so one 8 B read touches one line where two reads of two images touched two.
- Pixels: every comparison capture byte for byte the base's (0 levels, 0 pixels), demo-exit one frame per film, demo-surface, cabin-light, mirror-balls, bounce-raised-box, bounce-red-wall at 1080p.
- A first version that made the view depth image itself RG32UI, so that every reader read 8 B, was slower: the sector pass and its neighbours +0.084 ms (range +0.048 to +0.251) on the deck, because the depths around each step and the edges read twice the bytes.
- Cost, plain runs against the base build, 6 rounds of alternating order, the renderer's timer, the occlusion depth, sectors and denoise passes together: deck -0.071 ms per frame (range -0.129 to -0.008, load 5 to 9; a first run of the same change gave -0.094, range -0.113 to -0.042); floor window (120 frames from 2412) -0.137 ms (range -0.255 to -0.108), where the sector pass is bound more by the last-level cache (66 %).
- Tests: OcclusionImagesTests pins the format; BounceLightRenderTests.TheDepthPassCopiesTheDiffuseLightTheViewDepthAndTheGeometricNormalOfEachTexelsRepresentativePixel now also pins the copy's depth bits to the view depth image's. A depth word written as z * 1.0001 fails it.

**2026-11-01T13:36:43Z**

Change 3, the skin's ambient part only at skin pixels (builder, 2026-11-01). Under SeparableSubsurface the ambient pass had two attachments: the HDR image and the diffuse image, which it loaded and stored over the whole screen to add zeros everywhere but at skin (an additive blend, decision 14 of docs/plans/skin-and-hair.md). Now it has the HDR image alone and adds D's ambient part with an imageLoad and an imageStore of the diffuse image at skin pixels, through the scatter set as set 3 of DescriptorLayouts.AmbientScatterLayout (ImageUse.StorageFragment, General layout). Memory traffic: 16.6 MB read and 16.6 MB written less per frame at 1080p. The same sum at the same stored precision, at the pixels whose result the image uses: the same method.
- Device features: a storage write in the fragment stage needs fragmentStoresAndAtomics, which NVIDIA, RADV, ANV, lavapipe and MoltenVK offer (Direct3D 11 requires unordered access views in the pixel shader). independentBlend lost its one user, so the renderer no longer requires it.
- Pixels: every comparison capture byte for byte the base's (0 levels, 0 pixels): demo-exit one frame per film, demo-surface, cabin-light, mirror-balls, bounce-raised-box, bounce-red-wall (captures/pjza/cap-ambient-store), and the skin review scene scripts/skin-and-hair.json, its named captures head, ear, hair, hair-fringed, hair-cutout, hair-glass (captures/pjza/skin-ambient-store). In the scene's head-timing frames, 43 pixels in 32 frames differ by 1 level, within 2 levels (the shader's single-float add rounds once, where the blend can round twice).
- Cost: plain runs of the deck window (120 frames from 399), HEAD 0381e7c0 against this build in 6 rounds of alternating order, the renderer's timer: the upsample and the ambient pass together -0.060 ms per frame (median of the per-round differences, range -0.077 to -0.008). The scatter rows pass after them +0.017 (range +0.010 to +0.022), so the four passes from the upsample to the scatter columns -0.042 ms (range -0.056 to +0.007). Load 4.7 to 5.6.
- Tests: SkinScatterRenderTests.TheDiffuseImageHoldsDOfSkinTheDirectDiffuseLightOfOtherLitPixelsAndTheSkinMask passes on the old pass and the new one; a mutant that adds the ambient part at every lit pixel fails it. DeviceFeatureTests.TheDeviceRequiresStoresInTheFragmentStage and ResourceUsesTests.AStorageImageInAFragmentShaderIsReadAndWrittenInTheGeneralLayoutOfTheFragmentStage are new.
