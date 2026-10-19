---
id: ter-mln3
status: in_progress
deps: [ter-bgk5, ter-vbrx]
links: []
created: 2026-10-19T01:22:33Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo, engine]
---
# Video through ffmpeg

Host and flake: ffmpeg-headless in the dev shell (the user approved the download: 43 store paths from cache.nixos.org, about 935 MB unpacked), CaptureTarget as a union (PngFolder, VideoFile), FfmpegVideoSink writing to ffmpeg's stdin (libx264rgb -qp 0 into Matroska), --video and --fps on the command line, exit code 5 when ffmpeg is missing, the failure handling, and the measurement of 600 frames. Design: docs/plans/blue-danube-demo.md, section 'Engine changes for the demo' (decided with the user 2026-10-18: video chosen on the command line, outlines as a view setting that a step changes, a numeric frame's outline is the sphere around its contents, one outline record per frame, the image test helper taken out of ter-rtxu now, colors and a legend card instead of labels). The last step updates ARCHITECTURE.md.

