---
id: ter-wwu7
status: open
deps: [ter-gmv8]
links: [ter-cikj]
created: 2026-11-03T23:39:41Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, portability]
---
# GPU pass timings on KosmicKrisp are impossible for four passes

Found on 2026-11-03 while ter-gmv8 ran the demo on KosmicKrisp (Mesa 26.2.4) with VK_ICD_FILENAMES set to its ICD: dotnet run --project src/Tertius.Demo -c Release -- --script scripts/lamp-hall-16.json --headless --out <folder> --video none. The run reached the end with exit code 0, but the GPU timings of four passes are impossible: occlusion upsample and reflection trace about 7.686e14 ms, ambient light and bloom about 3.392e9 ms, over 301 frames. The other passes read plausible times, for example the direct light at 3.49 ms and the frame span at 8.27 ms. At KosmicKrisp's timestampPeriod of 41.67 ns, 7.686e14 ms is about 1.8e19 ticks, near 2^64, so the end timestamp of such a pass is probably a little earlier than its begin timestamp and the difference wraps. Sort it as ter-cikj sorts KosmicKrisp failures: a defect of ours against the Vulkan spec (for example a timestamp written at a stage or in an order that the spec does not promise), or a KosmicKrisp defect with a minimal reproduction.

