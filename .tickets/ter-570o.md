---
id: ter-570o
status: closed
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


## Preserved parent coordination notes

**2026-11-05T21:15:54Z**

2026-11-05: prerequisite ter-s7c6 merged, closed and pushed normally; main/origin/main23d603077ac306722cd1c4e55f8459480fc8b1e8 verified remotely, tested combined code-inputba5127acef21489cc12c01efc090351788f7c0ef unchanged. Starting ONE bounded capture/evidence workflow, not another implementation/review cycle. User additionally requires visible measured render-FPS and actual internal-resolution counters in the moving video, with output resolution shown too; encoded playback FPS must not masquerade as rendering FPS. Assignment /private/tmp/tertius-integration-20261105/ter-570o/task.md. Fresh video allocation uses capacity released by FSR hardening; standalone limiter allocation is separately released. Source/prototype imports and lighting retuning excluded. Parent retains root ownership; worker gets its own T3-created ticket worktree.

**2026-11-05T21:17:12Z**

2026-11-05: capture workflow ADMITTED runb1696083-f524-4e68-9100-c34b1d107510, version6b9d32e6-9541-43ae-b77f-999aa2750df0, source/schema SHA25637405a312b93b08932e35f22e90471f8103422f2bb8009179fab979cdb010bc6. T3 new worktree branchter-570o-fsr-controller-video at exact pushed main23d603077ac306722cd1c4e55f8459480fc8b1e8, Codexgpt-6.1-sol reasoningEffort=xhigh. Single capture/evidence agent only, no routine review/fix/hardening. User-visible measured render-FPS and actual internal/output-resolution counters explicitly included. One fresh allocation, Nix/normal GPUqueue/5GiB guard/<=1GiB new evidence, direct parent completion+release, no auto-delivery destination requested because existing grants forbid it. Final clip/proof still pending; admission is not controller/video acceptance.

**2026-11-05T21:19:24Z**

2026-11-05 capture member acknowledged and independently verified T3-bound /Users/alexispurslane/.t3/worktrees/tertius/ter-570o-fsr-controller-video, branchter-570o-fsr-controller-video, clean exactHEAD23d603077ac306722cd1c4e55f8459480fc8b1e8. Member mcp:launch:1172774f6837bb2d9c6f5dde09653fc2918384601504cd0a2c6e1d925b38608d has read full settled assignment and takes the ONE assigned video allocation. Matching binaries are read-only; normal Nix/GPUqueue,<=2 bounded attempts, no production edits/parent writes/children/workflows; explicit final allocation release required. Actual video and measured render-FPS/internal-resolution overlay proof remain pending, not claimed by this readiness receipt.


## Parent verification and integration

2026-11-05T21:48:36+00:00

The parent fast-forwarded main to the delivered immutable documentation commit bfe92955836c81d7c2eb767d4df76c4c5d4cdd56 before ticket closure. The capture workflow b1696083-f524-4e68-9100-c34b1d107510 reports terminal success, settled members and complete cleanup. No new capture, implementation, review cycle or workflow was needed.

Independent parent checks passed all 50 artifact SHA-256 hashes and all 300 frame/image/GPU-feedback/controller pairings. Every selected scale matches the preceding post-measurement scale. The retained timestamps reproduce every displayed Render FPS value exactly. The clip contains 111 internal-size transitions across 25 sizes, with 92 transitions after playback second one. Output and framebuffer dimensions remain 1920x1080. Internal dimensions range from 968x544 through 1920x1080. Only the startup frame resets history.

The parent inspected decoded frames 0, 150 and 299, then showed the MP4 inline with the saved charts and three captioned stills. The visible counters show measured completed-frame Render FPS and actual internal/output dimensions. Capture/readback/encoding/evidence overhead affects Render FPS. The 30-FPS playback is not a live-performance claim. Startup brightening, late blue-white glare and floor noise remain visible. This delivery neither imports nor accepts the separate noise prototype.

The unchanged production build-input identity remains ba5127acef21489cc12c01efc090351788f7c0ef. This integration changes only documentation and ticket metadata, so code suites were not repeated. Normal commit and push hooks remain required. Linux runtime, uninstrumented live performance and final lighting quality are not established by this video.

All root dirty files were archived with verified hashes at /private/tmp/tertius-integration-20261105/ter-570o-parent-delivery-20261105-164800. The three original parent note entries are preserved above. The unrelated ter-69s8, ter-7tnv and ter-icn3 ticket edits and nohup.out remain byte-identical. The worker released its heavy allocation and reports no background processes. The ticket closes for the requested video delivery only.
