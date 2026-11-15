---
id: t1-stwp
status: in_progress
deps: [t1-n8x9]
links: []
created: 2026-11-14T23:06:20Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-kh4g
tags: [rendering, experiment, performance]
---
# Stage 1b cost: the back layer in the depth pyramid against main, on the RTX 2000 Ada and the M1 Max

This ticket is an experiment, with no code for the branch. It uses the probe of t1-n8x9: read `docs/research/back-face-depth-layer/stage-1b/probe.md` and that ticket's terms first. Alexis chose the layer as the slab, stored in the depth pyramid. This ticket measures what that store costs per frame. The side image of Design 2 is out.

## Design of the experiment

A side is one build that the measurement compares. There are three sides:

| Side | Tree | What its difference to main holds |
|---|---|---|
| main | The commit ab54c785e | Nothing: the base |
| store | The probe with `SlabTenPercent` | The back-face pass, the wider finest texel and the changed build. The walks are main's walks. |
| slab | The probe with `SlabLayer` and `NoBackSheet` | The same, plus the changed walks: rays stop earlier or later than under the 10 % rule |

The difference of slab to store is the cost or the gain of the changed walks alone.

Scenes, all at an output of 1920 × 1080 through FSR 3.1, which renders 1280 × 720:

- The deck: `scripts/temporal-upscale-timing.json`.
- The cabin: `scripts/ter-kh4g-cabin-timing.json`.
- `scripts/ter-kh4g-rect-lamps-timing.json`.
- One view of dense geometry, with a timing script of your own from `scripts/station.json` or `scripts/demo-meshes.json`. Stage 1 timed the pass only in rooms and hulls of boxes.

Fix the exposure: run main once per scene, read the stops in `exposure.json`, and pass them to every side with `--exposure <stops>`. A side that changes the image otherwise changes the exposure and each lamp's reach (ticket t2-bne2).

### On the RTX 2000 Ada

Run the three sides in one cloud job per scene group, with `tools/runpod/ab_run.sh` and `tools/runpod/ab_report.py`, five runs per side. Stage 1's two jobs are the model: the record's section "The cost", and the job log. The budget of this ticket is $1.00 by the job log's estimates, through `tools/runpod/gpu_job.py` only.

### On the M1 Max

Run interleaved rounds: one round runs every side once, and the order turns from round to round. Run at least five complete rounds per scene. A take is one hold of `tools/gpu-lock.sh` in exclusive mode. Keep a take at or below about 20 minutes: split by scene. Stage 1's method is in the record under "On the M1 Max", and its scripts are in `/private/tmp/ter-kh4g/tools/` (`mac-rounds.sh`, `mac-report.py`, `mac-table.py`). They ran one binary in three modes. Change copies of them for three binaries. Use frozen Release binaries, `--in-flight 2 --pass-timestamps off`, and `tools/gpu-clock.sh` with `GPU_CALIBRATION_BIN=/private/tmp/tertius-survey-20261109/clockbin`. Record the GPU clock of every run, and drop a round whose runs did not all run at the same clock.

The GPU queue is shared with the other line and with the pictures ticket t1-gxzg. A holder in shared mode blocks a take, and the queue is first in, first out. In stage 1 two sets of rounds ended at the limit of the background command with too few rounds. So start a take, wait for it in the foreground of a background command with a long timeout, and count complete rounds, not started ones.

### What to report

For each scene and machine:

- The frame time of main: the mean and the spread from the fastest run to the slowest.
- Store minus main and slab minus main: the mean of the per-round differences, and their spread.
- The own time of the back-face pass and of the depth pyramid's build, where the run reports passes.
- The run count behind each number.

Also report the memory at 1280 × 720: the finest level as R32F and as RG32F, and the back share image.

Say plainly where a difference is inside the run-to-run spread. Do not round a difference toward the plan.

## Deliverables

- `docs/research/back-face-depth-layer/stage-1b/cost.md`: one table per machine, the commands, the job ids, the cloud dollars from the job log, and what was not measured. Keep measured facts apart from your reading. Plain English, as the lint hook asks.
- The raw runs under `/private/tmp/ter-kh4g/1b/cost/`, named in cost.md.

## Acceptance

- Both machines have a table with main, the two differences and the spread, from at least five runs or rounds per side and scene.
- cost.md states each number's run count and each run that was dropped, with the reason.

## Out of scope

Any tuning of the probe for speed, the side image, the design document and the real mechanism.


## Notes

**2026-11-14T23:56:25Z**

Launched 2026-11-14 from 24a320541 as a ticket lead on Claude Opus 5.5 at high effort, in its own worktree. clientRequestId: ter-kh4g-1b-t1-stwp-lead-1. taskId: node:delegated-task:command%3Amcp%3A3579fc31-496e-48d6-a7df-6042784d08cb%3Adelegate-task%3Ater-kh4g-1b-t1-stwp-lead-1. childThreadId: thread:delegated-task:command%3Amcp%3A3579fc31-496e-48d6-a7df-6042784d08cb%3Adelegate-task%3Ater-kh4g-1b-t1-stwp-lead-1.

**2026-11-14T23:56:25Z**

Answer of the line orchestrator in the lead's brief: the probe has a mode off (the new store without the pass), so the measurement takes four sides where time allows: main, layer, ten and off. The side off is the first to drop.

**2026-11-15T00:41:59Z**

Measured on both machines: docs/research/back-face-depth-layer/stage-1b/cost.md. Card, rounds 2 to 5: layer minus main is +0.031 ms on the deck, +0.055 ms in the cabin, -0.109 ms in rect-lamps and +0.446 ms in the crowd. Mac: +0.060 ms (inside the spread), +0.140, -0.141 and +1.014 ms. In the crowd of 2.0 million skinned vertices the pass alone costs 0.45 ms on the card and 0.80 ms on the Mac. Cloud cost $0.2047.
