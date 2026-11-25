---
id: t88-hjjm
status: open
deps: []
links: []
created: 2026-11-25T09:10:12Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [performance, rendering, demo]
---
# A headless film frame after 6 or more ticks is a still and costs 33 draws: 19 % of the Blue Danube film's frames take 71 % of its wall time

The headless renderer treats a draw after a clock step of at least 0.09 s (`FrameHistory.MeteringStepSeconds`, 5.4 ticks) as a still (decision 32). A still resets every history and draws 32 warm-up draws (`RendererOptions.DefaultWarmUpFrames`) before its shown draw. So each frame of a film step with `every` ≥ 6 costs 33 draws. In `scripts/blue-danube.json` that is 10 sections and 6,495 of the 33,688 frames: climb-high, phasing, night, transfer-night, transfer, closing, ring-approach, glide, ride-side and ride.

Measured on two instrumented full runs on Runpod (EPYC 7352, RTX 2000 Ada, 1920x1080, FSR 3.1 at 2/3, real encoder):

- The still sections take 1,761 s and 1,919 s of the 2,483 s and 2,692 s of wall time. Their draw time is 239 ms per frame, against 14.9 ms per frame in the other sections.
- A tick costs 0.37 to 0.60 ms on average in the still sections. All 332,516 ticks take 177 to 180 s.
- A Mac A/B on one camera: `every` 4 and 5 draw in 18.6 and 18.8 ms per frame, `every` 6 and 8 in 599 and 606 ms.

This ticket first said that the ring approach and the lift ride simulate at 46 to 66 ms per tick. That was wrong. It came from wall time per frame with one draw per frame assumed. docs/research/t88-hjjm-film-profile/README.md has the method, the tables and the limits of the evidence.

A window never draws a still. So this is a cost of headless time-lapse films only, not of real-time play.

Not decided, and left for a decision after this evidence (user, 2026-11-25: "leave optimization for after evidence"):

- whether a film's time-lapse frame should be a still at all;
- whether it should take fewer warm-up draws;
- whether the films should keep the cost.

The visual effect of each choice on the time-lapse sections is not measured yet.

## Notes

**2026-11-25: film and profile run.** The scratch instrumentation is commit cc1d04090 on `t88-hjjm-scratch-profile`, not for merge. It records World.Tick's parts, the physics step's phases and Jolt counters, the SyncProfile steps, the capture, the sink write, the garbage collector and the CPU time per section, and one CSV row per tick. It also writes the readout log in the same run.

Runs and costs, as the workbench counted them:

| Job | What it did | Cost |
|---|---|---:|
| Worker test | the encoder, the transcode and the relay | $0.0449 |
| Film run A | rendered and profiled; the transfer was lost because the Mac receiver's runpodctl had no GC root | $1.0439 |
| Film run B | rendered, profiled and delivered | $0.5788 |
| Total | | $1.6676 |

Each job used its own temporary endpoint, which was deleted and confirmed gone.

The film is `~/Movies/Blue Danube/blue-danube-2026-11-25.mp4`: 578.48 s, 34,709 frames, 1920x1080 at 60 fps, H.264 faststart, with the US Marine Band recording. A phone copy at 720p and the recoverable yuv444p master with its readouts lie beside it. The recording ends at 549.06 s, so the last 29.4 s are silent.
