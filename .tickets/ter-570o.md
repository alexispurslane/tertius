---
id: ter-570o
status: in_progress
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


## Notes

**2026-11-05T21:38:07Z**

Capture delivered on the owned ter-570o-fsr-controller-video branch from exact base 23d603077ac306722cd1c4e55f8459480fc8b1e8. Report: docs/research/ter-570o-controller-video.md. Entire settled brief preserved verbatim in docs/research/ter-570o-capture-assignment.md. One bounded attempt: real demo-exit deck, 300 frames, 10.000 seconds, actual fixed 1920x1080 output, production Fsr3Upscaling.Default / FrameTimeTarget controller, 111 genuine internal-size changes (92 after playback second one), 25 sizes, and image-paired completed-frame Render FPS/Internal/Output overlay data. Evidence: captures/ter-570o/evidence/ter-570o-fsr-controller.mp4, frames.jsonl, analysis.json, provenance.txt, original/decoded stills and queue logs. Capture overhead is included and documented; 17 PNG evidence saves account for 7.83 real seconds. Source startup brightening and late blue-white glare/noise are retained, not fixed. No production/test edits, prerequisite imports, suites, hardening, children/workflows, merges, pushes or closure. Await parent integration. Allocation release follows directly after all owned commands and the normal report commit finish.
