# Where the Blue Danube film run spends its time (t88-hjjm)

The ter-9wwx acceptance run rendered the Blue Danube film at about 13 frames per second of wall time. Ticket t88-hjjm first blamed the simulation, because its frame times rose with the ticks between frames. This study measured the run from the inside. The simulation is not the cause. The headless renderer's still rule is: every frame of a film step with `every` of 6 or more costs 33 draws.

## Terms

- **Film step**: a step of `scripts/blue-danube.json` that runs `every` ticks and then captures one frame, `frames` times. A tick is 1/60 s of simulation.
- **Section**: one film step, named by its `name`, such as `ring-approach`.
- **Tick time**: the wall time of one `World.Tick` call on the ticking thread.
- **Draw time**: the wall time of `playback.Capture` minus the video sink write. It holds the render snapshot, the renderer's CPU work and the wait for the GPU.
- **Sink write**: the wall time of `FfmpegVideoSink.Take`, the write of one frame's pixels into ffmpeg's input. It blocks while the encoder is behind.
- **Still**: a headless draw after a clock step of at least `FrameHistory.MeteringStepSeconds`, 0.09 s (5.4 ticks). A still resets every history and draws `RendererOptions.WarmUpFrames` warm-up draws, 32 by default, before its shown draw (`FrameHistory.DrawsOf`, decision 32 of docs/plans/temporal-renderer.md). A window never draws a still.

## Method

Scratch instrumentation, commit cc1d04090 on the branch `t88-hjjm-scratch-profile`, not for merge:

- `World.Tick` is split into prepare, gameplay, the wait for the physics thread after gameplay, the sync point and the log flush.
- The physics thread's `JoltPhysicsBackend.Step` is split into its phases: frames, hulls, intents and gravity, characters, the Jolt system updates, contacts, hand-offs, ghosts and the writes. It also counts the Jolt systems that updated, their active rigid bodies and the characters, and keeps the slowest single system update.
- The build sets `SyncProfile=true`, so the sync point's 30 step sums of `SyncProfile` are compiled in.
- The script runner records the capture, the sink write and the readout row. At each section's start and end it also records the garbage collector's counts, pause time and allocated bytes, and the process's CPU time.
- Output: one JSON row per section (`profile-run-a.json`, `profile-run-b.json`) and one CSV row per tick (332,516 rows, kept beside the film's master).

Two full runs of the film were made on Runpod workers, each with an AMD EPYC 7352 (48 threads) and an RTX 2000 Ada. Both ran at 1920x1080 with FSR 3.1 at the fixed 2/3 scale, with the real video encoder (libx264rgb, QP 0). Run A was the first film job, whose transfer failed. Run B made the delivered film. The two jobs ran on different workers.

`analyze.py profile-run-b.json` prints every table below.

## Result 1: 71 % of the wall time is the still sections' draws

| Group of sections | Sections | Frames | Ticks | Wall, run A | Wall, run B | Draw time A / B | Sink write A / B | Tick time A / B |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `every` ≥ 6 (stills) | 10 | 6,495 | 278,308 | 1,761 s | 1,919 s | 1,553 / 1,714 s | 75 / 77 s | 131.3 / 125.7 s |
| `every` < 6 | 53 | 27,193 | 54,208 | 722 s | 773 s | 405 / 441 s | 268 / 280 s | 48.2 / 51.5 s |
| Whole film | 63 | 33,688 | 332,516 | 2,483 s | 2,692 s | 1,958 / 2,155 s | 343 / 357 s | 179.5 / 177.3 s |

The stills are 19 % of the frames and take 71 % of the wall time. Their draw time is 239 ms per frame in run A. The other sections' draw time is 14.9 ms per frame. All 332,516 ticks together take 7 % of the wall time.

In ring-approach, glide and the two lift sections, the process uses 0.14 to 0.19 s of CPU time per second of wall time. In the other still sections it uses 0.29 to 0.35 s. So the process waits most of the time, which fits draws that wait for the GPU.

## Result 2: the threshold is the still rule

A Mac run filmed one camera at four values of `every`, 30 frames each, with `--video none`:

| `every` | Clock step | Draw per frame |
|---:|---:|---:|
| 4 | 0.067 s | 18.6 ms (18.4 ms on a repeat) |
| 5 | 0.083 s | 18.8 ms |
| 6 | 0.100 s | 599 ms |
| 8 | 0.133 s | 606 ms |

The jump is about 32 times. It lies between 5 and 6 ticks, where the clock step passes 0.09 s, and so it matches 33 draws per still. The tick time stayed at 0.2 to 0.5 ms in all four.

## Result 3: the named sections against nearby fast ones

Run B, in ms per tick, except where a column says per frame:

| Section | every | Tick | Gameplay | Wait for physics | Sync point | Physics step | Jolt update | Characters | Draw per frame |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| glide-window (fast) | 4 | 0.61 | 0.07 | 0.36 | 0.17 | 0.38 | 0.28 | 0.00 | 17.0 |
| ring-approach | 6 | 0.60 | 0.09 | 0.30 | 0.19 | 0.32 | 0.21 | 0.01 | 386.8 |
| glide | 6 | 0.56 | 0.07 | 0.30 | 0.18 | 0.32 | 0.21 | 0.00 | 287.8 |
| shaft (fast) | 1 | 1.30 | 0.09 | 0.90 | 0.28 | 0.93 | 0.54 | 0.17 | 17.1 |
| ride-side | 8 | 0.48 | 0.06 | 0.25 | 0.16 | 0.26 | 0.09 | 0.05 | 386.6 |
| ride | 8 | 0.48 | 0.06 | 0.25 | 0.15 | 0.25 | 0.11 | 0.04 | 359.7 |
| foot (fast) | 1 | 1.30 | 0.09 | 0.89 | 0.29 | 0.89 | 0.47 | 0.12 | 15.3 |

- **Where the tick goes.** The physics thread runs its step in parallel with gameplay. The ticking thread then waits for the rest of that step. So "wait for physics" is the physics step minus gameplay, and the tick is about gameplay + wait + sync point.
- **Fast sections cost more per tick.** The fast sections near the slow ones cost the same or more per tick. In the ring approach and glide, the Jolt update is 0.21 ms per tick: 3 systems update, with 1.7 active rigid bodies on average. The lift ride's sections, at 0.48 ms, are the cheapest ticks of the station.
- **Per-tick quantiles.** The tick CSV's quantiles agree. The median tick is 0.43 ms in ring-approach and 0.35 ms in ride, and the 99th percentile is 2.1 to 2.4 ms in every one of these sections. The slowest single tick in them is 6.9 ms (ring-approach) and 5.6 ms (ride).
- **The sync point.** Its largest steps here are the render snapshot (0.05 to 0.08 ms per tick) and the physics queries (0.01 to 0.06 ms).
- **GC.** The ride has 2 gen-0 collections and 1 gen-1 collection over 4,312 ticks: 6.4 ms of pause and 14.7 MB allocated. The other named sections are alike. No gen-2 collection falls in them.
- **The sink write.** It is 10 to 11 ms per frame everywhere: the encoder at QP 0 takes about that long per 1080p frame on this worker. It blocks the ticking thread, because the write is synchronous.

## Result 4: how stable the timing is

The two runs ran on different workers of the same type.

| Measure | Run A | Run B |
|---|---:|---:|
| GPU render median | 10.11 ms | 10.74 ms |
| GPU capture pass median | 2.19 ms | 2.87 ms |
| Wall time, whole film | 2,483 s | 2,692 s |
| All ticks | 179.5 s | 177.3 s |

- The ratio of B's wall time to A's, per section, has a median of 1.072 and lies between 0.70 and 1.20.
- The sections rank almost the same by wall time in both runs (Spearman's rank correlation 0.996). Neighbours such as glide and closing swap places. The still sections dominate in both.
- The tick totals differ by 1.2 %. The draw totals differ by 10 %, which follows the GPU.

## What the evidence establishes, and what it does not

It establishes:

- The slow sections are slow because of the number of draws per frame, not because of the simulation, the physics, the garbage collector or the encoder.
- On this worker, the ticks of the four slow sections have medians of 0.35 to 0.46 ms and 99th percentiles of 2.1 to 2.3 ms. The worst single tick is 6.9 ms. A 60 Hz tick has 16.7 ms.
- The threshold lies between 5 and 6 ticks per frame (the Mac A/B), as the still rule predicts.

It does not establish:

- The GPU time of a warm-up draw. `gpu-timings.json` holds only the shown draws. 33 draws per still is the code's rule and fits the measured ratio of about 32, but the run did not count draws directly.
- Where the time goes inside Jolt's `PhysicsSystem.Update`. The study timed the call and counted bodies, and took no native samples. At 0.1 to 0.3 ms per tick, no sample was needed for this question.
- Real-time play in a window. A window draws every tick and never draws a still, so the tick numbers bound the simulation's share. The draws of a window run were not measured here.
- The cost of the instrumentation itself. It reads the clock about 25 times per tick, and SyncProfile is compiled in. No uninstrumented run with the real encoder exists to compare with.
- A stable timing file across workers. Two workers of the same type differed by 7 % in wall time and by 6 % in the GPU median. A comparison of timings needs the same worker, or several runs on each side.

## The correction

ter-9wwx's note of 2026-11-25 and the first text of t88-hjjm said that the ring approach and the lift ride simulate at 46 to 66 ms per tick. That was an inference from wall time per frame, which assumed one draw per frame. The ticks cost 0.37 to 0.60 ms on average. The rest was 32 extra draws per frame.

## The film

The same run B made the delivered film. Its files are outside the repository:

- `~/Movies/Blue Danube/blue-danube-2026-11-25.mp4` (main film) and `blue-danube-2026-11-25-phone.mp4` (phone copy).
- `~/Movies/Blue Danube/master-2026-11-25/` (the master, the readouts and the profile files).

The path from run to film:

1. The real encoder wrote a lossless master of 37.0 GB with 33,688 frames.
2. The worker transcoded it to libx264 yuv444p at CRF 10 (2.69 GB, 33,688 frames) and sent it through runpodctl's relay (75 s).
3. The run wrote its readout log from its own captures. On the Mac, the same code gave a log byte for byte equal to a separate `--readouts` run.
4. BlueDanubeEdit made the film on the Mac, where its macOS fonts live. The film is H.264 at 1920x1080 and 60 fps, faststart, 578.48 s and 34,709 frames, with the US Marine Band recording.
5. The recording lasts 549.06 s, so the film's last 29.4 s are silent.
