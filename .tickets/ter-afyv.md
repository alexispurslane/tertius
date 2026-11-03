---
id: ter-afyv
status: closed
deps: []
links: []
created: 2026-11-03T01:57:54Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, spike]
---
# Spike: time a Metal ray trace of the demo scenes on the M1 Max

Decides between reflection designs A and B of the epic. See the note of 2026-11-02 for the measurement, the views and the decision rule.


## Notes

**2026-11-03T01:58:14Z**

The spike's scope, set by the orchestrator on 2026-11-02 after the user asked for the spike.

Question: how much GPU time does a world ray trace take on the development Mac (Apple M1 Max)? The M1 has no ray tracing hardware, so Metal walks the BVH (bounding volume hierarchy) in shader code. MoltenVK 1.4.2 offers no ray query but offers VK_EXT_metal_objects (vulkaninfo, 2026-11-02).

What the spike builds. The code stays on the ticket branch and is not merged:
1. A native Objective-C library in native/metal-trace/, built by a Nix derivation native/metal-trace.nix, the way native/joltc.nix builds joltc. The dev shell names its folder in TERTIUS_METALTRACE_DIR. The Metal shaders compile at run time from source (newLibraryWithSource), so no Xcode compiler is needed.
2. The renderer enables VK_EXT_metal_objects only when the device offers it and the spike runs. It exports the MTLDevice and the Metal buffers under GeometryStore's vertex and index buffers, the deform region included, and the data that gives each instance's roughness. Follow the extension's spec, for example the create info that it requires at allocation. Geometry does not pass through the CPU.
3. A demo option, --metal-trace-spike <folder>, for a headless run. After each frame's Vulkan work completes, the library does this work:
   - It builds one BLAS for each static mesh once.
   - It refits or rebuilds the BLAS of each deformed mesh in every frame.
   - It builds the TLAS in every frame from the frame's opaque instances, with GpuInstance's camera-relative model rows.
   - It traces the rays below and writes one hit record for each ray: instance, triangle, barycentric coordinates and distance. It shades nothing.
   - Cutout instances count as opaque, with no alpha test. Glass, puffs, particles and impostor billboards stay out. The report counts what stayed out.
4. Workloads at a render size W x H. Each one is timed on its own:
   - primary: one camera ray for each pixel. It stands in for the G-buffer and is not part of B's cost.
   - reflection: one ray for each pixel with a primary hit. Its direction is a GGX sample of visible normals, with the material's roughness, around the interpolated vertex normal.
   - diffuse quarter: one cosine-weighted ray for each 2 x 2 block of pixels.
   - diffuse full: one cosine-weighted ray for each pixel, for information.
   - the TLAS build, and the BLAS updates of deformed meshes.
   - once: the static BLAS builds, with their time and memory.
   Random numbers come from a hash of the pixel and the frame number, so two runs give the same rays.
5. Timing: the GPU time of each workload from Metal, either command buffer GPUStartTime and GPUEndTime or counter samples at stage boundaries. Use 120 frames after a warm-up, and report the median and the 95th percentile. Hold the GPU lock, and state the load from uptime.
6. In-frame order: one run orders the Metal work against the Vulkan frames with an MTLSharedEvent that is exported from a Vulkan timeline semaphore. The frame's submit signals it. The Metal command buffer waits on it and signals a value that the next frame's submit waits on. Report the frame time with and without the trace. If MoltenVK cannot export the event, report the exact failure. That failure is a finding, and it does not block the timings of item 5.
7. Correctness, so that the timings measure the right scene:
   - The primary hits' distance agrees with the raster depth of the same frame within 1 %, on at least 99 % of the opaque pixels.
   - Debug PNGs of the primary hits' normals and of the reflection hits' distance, beside the engine's own capture of the frame. Look at each image.

Views: demo-exit (the step view whose frame took 14.31 ms), reflections, rigs-crowd (the most deformed meshes), and the demo script with the most triangles. Count the triangles to find it. Sizes: 1280 x 720, the render size of FSR 3.1's quality mode for 1080p output, and 1920 x 1080.

Baseline: from a run of main's code with the same views at 1920 x 1080 in Release, report the GPU time of the passes that B replaces. They are the reflection passes, the radiance pyramid, the environment map faces and their prefilter, and the screen-space bounce light, from gpu-timings.json.

Decision rule. The orchestrator proposed it on 2026-11-02, and the user can change it before the result. B is fast enough when, on the heaviest of the four views at 1280 x 720, these four take at most 3.0 ms median and 3.5 ms at the 95th percentile of GPU time: the TLAS build, the deformed BLAS updates, the reflection rays and the diffuse quarter rays. The reflection workload traces a ray for every opaque pixel. In B, only the screen trace's misses need a world ray, so this bound is conservative. Reasoning: at 60 fps the frame has 16.7 ms. At a 1280 x 720 render size, the raster passes, the hit shading pass, the two accumulators and FSR 3.1 need the rest.

Report: docs/research/metal-trace-spike.md holds the method, the tables, the images, the verdict against the rule, and the open items for B (alpha-tested hair, impostors, the hit pass, KosmicKrisp). The report and this ticket's note go in a final commit of their own, so that only they come to main. ARCHITECTURE.md does not change.

**2026-11-03T01:58:51Z**

Launched 2026-11-02: workflow run 112dbd8e-e665-46f0-84d8-244f9e675d78, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (build, one review, one fix), branch ter-afyv-metal-trace-spike from 8ebc19923.

**2026-11-03T02:55:07Z**

2026-11-02, the spike's result (branch ter-afyv-metal-trace-spike, report docs/research/metal-trace-spike.md). Verdict: B is not fast enough by the rule as written, so the renderer goes all in on design A.

The rule sum (TLAS build + deformed BLAS updates + reflection rays + diffuse quarter rays, per frame, median / p95 over 120 frames, Release, 1280 x 720): demo-exit antispinward 11.08 / 12.24 ms, reflections 1.77 / 1.88 ms, rigs-crowd 21.13 / 27.28 ms, lamp-hall-64 1.75 / 2.13 ms. A second run: 10.97 / 11.93, 1.76 / 1.78, 19.63 / 24.60, 1.54 / 2.07 ms. The heaviest view, rigs-crowd, is seven times the bound of 3.0 / 3.5 ms: its 100 deformed rigs take 16.94 ms of BLAS refits. demo-exit, the heaviest view by main's frame span, fails with no deformed mesh: 8.62 ms of reflection rays in a closed hall where every ray hits. All nine films of demo-exit fail (7.09 to 11.08 ms median). They fail only because of the reflection rays, which start from the demo's paint of roughness 0.38. Main starts no reflection ray there, because its limit is 0.37 (ter-8bda). Without the reflection rays, the TLAS and the diffuse quarter rays of the films take 1.55 to 2.91 ms in the sum of their medians, inside the bound. So if B traces no world ray from a surface of roughness 0.38, the verdict rests on the deformed BLAS refits of rigs-crowd alone.

The scene is the one on screen: the primary hits agree with the G-buffer distance within 1 % on at least 99.998 % of the surface pixels in all 24 timed runs. MoltenVK 1.4.2 exported the MTLDevice, the buffers and a timeline semaphore as a MTLSharedEvent with no failure. The in-frame run's one Metal command buffer takes 13.14 ms on demo-exit against 13.20 ms for the sum of the timed parts. On the other views it takes less than the timed sum: 0.87 ms less on lamp-hall-64, 1.66 ms on reflections and 5.31 ms on rigs-crowd. That is 0.015 to 1.06 ms per timed command buffer, so it is no fixed cost, and the spike did not measure its cause. The verdict holds in the in-frame run too: less the timed primary rays, its command buffer takes 15.90 ms on rigs-crowd and 11.02 ms on demo-exit. The baseline, main at 1920 x 1080, spends 4.0 to 7.5 ms on the passes that B replaces.

Choices made in the spike: the note's "step view whose frame took 14.31 ms" is not on record. In the tickets, "the step view" means pbr-spheres low (ter-eqtb), but the note puts it under demo-exit. So all nine demo-exit films were measured, and the report uses antispinward, the heaviest by the rule. pbr-spheres was not measured. It has no deformed mesh, so the verdict does not change. The script with the most triangles is rigs-crowd (3,962,316), already a view, so the fourth view is lamp-hall-64 (148,440). A count at 1280 x 720 in the review's fix stage gave the same ranking, with no impostor billboard in any script. The report lists every limit, among them the unread GPU clock, the gap of unknown cause between the timed and the in-frame numbers, the roughness of the demo's paint, and the raw run data, which was not kept.

**2026-11-03T03:42:13Z**

Integrated on 2026-11-02 by the orchestrator: main holds only the report (docs/research/metal-trace-spike.md, its images and count-1280x720.txt) and this note, from the docs-only commits 5f8b44b82 and 0f5d181f0. The spike code stays on the branch ter-afyv-metal-trace-spike at 0f5d181f0 (code commit 675b65020).

The orchestrator's reading of limit 5: demo-exit fails only because of reflection rays from the demo's paint of roughness 0.38, which main does not reflect (cutoff 0.37). The temporal renderer exists to reflect such surfaces, because their missing reflections are the user's main complaint (2026-11-02). So demo-exit's failure counts against B, beside rigs-crowd's deformed BLAS refits. By the user's rule of 2026-11-02, the renderer goes all in on design A.
