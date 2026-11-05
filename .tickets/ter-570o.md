---
id: ter-570o
status: open
deps: [ter-s7c6]
links: []
created: 2026-11-05T19:03:32Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, fsr, visual-acceptance]
---
# Show a real demo video with FSR and the live dynamic-resolution controller

User request, 2026-11-05: after FSR and dynamic resolution merge, show a continuous moving section of the real demo with both enabled and demonstrably working. This is a user-facing integration deliverable, not another implementation/review cycle. Start as soon as ter-s7c6 is merged; do not wait for unrelated box-projection or noise work.

## Design

Use the merged FSR pipeline and actual windowed dynamic-resolution controller. Headless fixed-scale capture alone does not demonstrate dynamic resolution. Keep output resolution fixed while the internal shaded dimensions change in response to actual frame-numbered GPU measurements. Select a moving demo-exit section with geometry edges and reflections/transparency where practical. Preserve current production lighting/noise policies; no noise prototype import or quality acceptance is implied.

## Acceptance Criteria

Show the video inline to the user, not only its path. Target a bounded 10–20 second section at 1920x1080 output when supported. Retain exact integrated commit, renderer settings, advancing simulation/render clocks, warm-up/reset history, FSR enabled status, and per-frame internal/output dimensions with controller GPU-time measurements. Verify genuine controller-driven internal-resolution changes during the clip; do not substitute fixed scaling or scripted size changes and call that live dynamic resolution. Inspect motion for trails, edge breakup, exposure jumps, reflections and size-change glitches; report visible limitations. Preserve actual timing provenance and distinguish encoded playback FPS from measured live performance. If output capture does not exercise the live controller, explicitly report the limitation rather than claim acceptance. Use Nix, normal GPU lock/capacity rules and the 5 GB free-disk guard; do not launch heavy work before an allocation is available.

