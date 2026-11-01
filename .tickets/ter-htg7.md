---
id: ter-htg7
status: open
deps: [ter-b2da]
links: []
created: 2026-11-01T22:08:21Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, fsr]
---
# Merge the FSR 1 experiment into main, then refactor and improve its code

The user's instruction (2026-11-01, relayed by the FSR experiment's thread): merge experiment-fsr1 into main as soon as all the correctness tickets are done (epic ter-b2da), then refactor and improve the FSR code, 'because this will be a huge benefit to the end of it'.

Result of the experiment (thread 'Experiment: FSR 1 upscaling (no ticket)', branch experiment-fsr1, head 3be9aef99, base 06050163e): FSR 1 at render scale 0.77 saves 2.02 ms per frame on demo-exit at 1920x1080 (13.9 % of 14.5 ms), from 13 alternating clean runs with a 1-minute load of 4 or less and no other GPU job. 0.87 saves 1.58 ms and 0.9 saves 1.54 ms. The user's bar was 1 ms, and the user judged the image at 0.77 as fine. A Metal System Trace of 300 frames shows every screen-sized pass 20 to 40 % cheaper at 0.77; the fixed-size passes stay the same; FSR's two passes cost 0.56 ms. Recommended setting Fsr1Upscaling.Default: scale 0.77, sharpness 1 stop. The renderer's default stays native (NoUpscaling). Demo flags --fsr off|<scale> and --fsr-sharpness <stops>. Evidence: /tmp/fsr1/runs/*/load.txt and run.log, /tmp/fsr1/trace/, stills in /tmp/fsr1/final and /tmp/fsr1/final77, videos in /tmp/fsr1/video (copy what must survive into captures/ before /tmp is cleared).

Where FSR visibly loses (3x crops): tiny bright lights merge and get glows, and at 0.77 some disappear; screen-space reflections get larger steps; thin struts soften; thin far geometry breaks up differently; the bloom's glow is 1.3 times wider at 0.77 (a bloom property, ticket of its own).

The merge: merge main into the branch first (the correctness tickets will conflict, most likely in VulkanRenderer.cs, RenderTargets.cs, DescriptorLayouts.cs and the reflection and occlusion code). Every screen-sized image of a new pass must follow the render size (RenderTargets.Extent), not the output size (OutputExtent), and every quantity in pixels must count render pixels, except in the overlay. Update ARCHITECTURE.md: the render size and the output size, the frame graph's stage Upscale between AntiAliasing and Overlay, FrameContext.RenderResult, and the mip bias as specialization constant 4. Run the full Checked and Release suites, and look at FSR captures again.

The refactor and improvements: (1) 0.87 against 0.9: check whether a render size rounded to a multiple of 8 gives 0.87 its expected saving (1670x940 are not multiples of 8); (2) the long methods that the branch grew: the VulkanRenderer constructor (61 lines), DrawFrame (62), CreatePools (64) and ScriptCommand's RendererOverrides.ApplyTo (complexity 11); (3) the hook's long-tuple-unpacking findings in DescriptorLayouts (graphics layouts with 8 values, compute layouts with 6); (4) the surviving hand mutation, the RCAS input clamp in fsr1_rcas.frag, which a lavapipe test could cover; (5) coverage and mutation testing, which the experiment skipped. Ask the user before turning FSR at 0.77 on by default in the demo: the user said 'if it saves 1 or more ms, seems worth it to me', but the default is the user's call.

