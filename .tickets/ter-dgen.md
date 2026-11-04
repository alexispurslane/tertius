---
id: ter-dgen
status: open
deps: []
links: [ter-wwu7, ter-cikj]
created: 2026-11-04T02:25:08Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, portability, upstream]
---
# KosmicKrisp writes a timestamp at the start of its Metal compute encoder, and 0 from the fifth stamp in one encoder


Found on 2026-11-03 by ter-cikj. This is the cause of ter-wwu7. It is a defect of KosmicKrisp (Mesa 26.2.4), not of the renderer: the renderer's stamps follow the spec, and MoltenVK gives sane values for the same commands.

## The reproduction

`docs/research/kosmickrisp/timestamps.c` writes a stamp before a list of passes and one after each pass, in one command buffer and one submission. A pass is a compute dispatch that loops (C) or a render pass that clears a 2048 x 2048 image (R), with a global barrier after it. Every stamp is `vkCmdWriteTimestamp2` at `VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT`, outside any render pass instance. Build and run it in the dev shell, in that folder:

```bash
glslc -O -mfmt=num -o busy.inc busy.comp
cc -O1 -o timestamps timestamps.c -lvulkan
VK_ICD_FILENAMES=$TERTIUS_KOSMICKRISP_ICD ./timestamps CCRCCR 3
VK_ICD_FILENAMES=$TERTIUS_KOSMICKRISP_ICD ./timestamps CCCCCCC 1
```

On KosmicKrisp, with `CCRCCR`, every stamp after a compute pass reads the same tick as the stamp before the first compute pass of its run, and the render pass after them takes all their time (10.1 ms against MoltenVK's 0.05 ms). With `CCCCCCC`, stamps 0 to 3 read that tick and stamps 4 to 7 read 0, marked available. Every run of 5 or more stamps in a row outside a render pass shows the 0s. On MoltenVK 1.4.2, every compute pass takes 2.4 to 3.9 ms, and no stamp reads 0.

## The cause in Mesa's source

`kk_CmdWriteTimestamp2` (`src/kosmickrisp/vulkan/kk_query_pool.c`) records a stamp outside a render pass into the command buffer's open compute encoder, `cmd->pre_gfx`, which collects all compute work and stamps until the next render pass. `mtl_compute_write_timestamp` (`src/kosmickrisp/bridge/mtl_encoder.m`) writes it with `MTL4TimestampGranularityRelaxed`, which Apple documents as a stamp that can sample at an encoder boundary. The measured values agree: the stamps of one encoder read its start. The 0s from the fifth stamp on point to a limit of four samples per encoder.

## The spec

The spec (`vkCmdWriteTimestamp2`, chapter "Queries") says: "Any timestamp write that happens-after another timestamp write in the same submission must not have a lower value unless its value overflows the maximum supported integer bit width of the query." A stamp at all commands happens after the stamp before it, so a 0 after a stamp of 81,595,672,574,186 breaks this rule. A stamp that reads its encoder's start also reads a time before the work that its execution dependency puts before it.

## What to do

Report it to Mesa's KosmicKrisp maintainers with the reproduction. That is an outward-facing step for the user to approve. Until a fixed Mesa reaches the pinned nixpkgs, the per-pass times and the span of a run on KosmicKrisp are not valid. The span is at best a lower bound: the last stamp misses the work of its own encoder. `GpuTimingTests` and `ScriptCommandCaptureTests.AHeadlessFilmWritesTheGpuSpanOfItsFramesBesideThePassesWithAMeanNoShorterThanItsLongestPass` fail on KosmicKrisp because of it, and stay as they are.
