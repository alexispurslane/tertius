# The cost of stage 1b: the back layer in the depth pyramid against main

This is the record of ticket `t1-stwp`, of 2026-11-14 and 2026-11-15. It measures what the chosen model costs per frame against main, on the RTX 2000 Ada and on the M1 Max. The chosen model is the back layer as the slab, stored in the depth pyramid. Every run used the frozen probe of [probe.md](probe.md). The terms are those of that record.

## The sides

A side is one build or mode that the measurement compares.

| Side | Mac binary | Cloud tree | What its difference to main holds |
|---|---|---|---|
| main | `/private/tmp/ter-kh4g/1b/main/` | `ab54c785e` | Nothing: the base |
| `layer` | `/private/tmp/ter-kh4g/1b/probe/`, mode `layer` | `refs/scratch/ter-kh4g-1b-layer` | The back-face pass, the wider finest texel, the changed build and the changed walks |
| `ten` | The probe, mode `ten` | `refs/scratch/ter-kh4g-1b-ten` | The pass, the wider texel and the changed build. The walks keep the 10 % rule. |
| `off` | The probe, mode `off` | `refs/scratch/ter-kh4g-1b-off` | The wider texel and the changed build, without the pass |

The sides `layer` and `ten` match the sides "slab" and "store" of the ticket. The mode `off` is a fourth side, which the probe made possible. The differences split the cost:

- `layer` minus main is the cost of the chosen model.
- `off` minus main is the wider texel and the changed build alone.
- `ten` minus `off` is the pass alone.
- `layer` minus `ten` is the changed walks alone.

The probe's control 2 showed that `ten` and `off` give main's image byte for byte. So their frames do main's work, apart from the store and the pass.

## The scenes and the fixed exposure

Every run films 120 frames at an output of 1920 × 1080 through FSR 3.1, which renders 1280 × 720.

| Scene | Script | Scene of the demo |
|---|---|---|
| Deck | `scripts/temporal-upscale-timing.json` | `demo-exit`, the deck camera |
| Cabin | `scripts/ter-kh4g-cabin-timing.json` | `demo-exit`, the cabin camera |
| `rect-lamps` | `scripts/ter-kh4g-rect-lamps-timing.json` | `orbit`, the person's camera |
| Crowd | `scripts/ter-kh4g-crowd-timing.json`, new in this ticket | `rigs-crowd`: 100 skinned cylinders of 20,202 vertices each |

The crowd is the view of dense geometry. The ticket named `station.json` or `demo-meshes.json`. Both hold only boxes or a few thousand triangles. The crowd draws about 2.0 million deformed vertices per frame, so it tests the vertex work of the pass.

The exposure is fixed per machine and scene. The first run of main on each machine used the automatic exposure. The median stops of its `exposure.json` then went to every run of every side with `--exposure <stops>`.

| Scene | Stops on the card | Stops on the Mac |
|---|---:|---:|
| Deck | 0.7070702 | 0.7094591 |
| Cabin | −0.4267480 | −0.4218524 |
| `rect-lamps` | 2.9475819 | 2.9556005 |
| Crowd | −9.2693568 | −9.2687040 |

Every frame of all 80 cloud runs and all 192 Mac runs held its fixed exposure.

## The method

### On the RTX 2000 Ada

One cloud job ran `tools/runpod/ab_run.sh` with the trees main, `layer`, `ten` and `off`, five runs per side. Each scene's command built main and ran main once for the exposure. Then `ab_run.sh` warmed the card with one run of main and ran five rounds. A round runs every tree once, and the order reverses in every second round.

A run's value is its median GPU frame span without the `capture` pass, in ms. That pass is the headless readback over PCIe, as in `tools/runpod/ab_report.py`. A difference pairs two sides inside one round.

The deck ran in job A, and the other three scenes ran in job C. Each scene compares its sides with the main of its own job.

### On the M1 Max

A take is one exclusive hold of `tools/gpu-lock.sh`, inside the dev shell. Each take timed one scene with 12 rounds of the four sides, back to back. The takes lasted 7 to 9 minutes, and the lock went free between them.

- Round r takes row r mod 4 of a Williams square of the four sides. Over four rounds, each side runs once at each position and directly follows each other side once.
- Each take starts with one untimed run of main. The first take of a scene also takes its exposure from that run.
- Every run used `--video none --in-flight 2 --pass-timestamps off` and ran beside the GPU clock sampler. The sampler is `tools/gpu-clock.sh` with `GPU_CALIBRATION_BIN=/private/tmp/tertius-survey-20261109/clockbin`.
- A run's value is its median GPU frame span, in ms. The Mac runs have no pass time stamps.
- For each round, the report compares the mean clocks of its four runs. It keeps the round only with an agreement within 1 MHz. The top state is 1296 MHz.

The 1-minute load at the runs' starts was 6.5 to 17.4, because other agents used the machine.

## The results on the RTX 2000 Ada

A difference cell gives the mean of the per-round differences, then the lowest and the highest. The mark ‡ means that the per-round range holds 0. All values are in ms.

All five rounds:

| Scene | Rounds | Main: mean (lowest to highest, spread) | `layer` minus main | `ten` minus main | `off` minus main | `layer` minus `ten` | `ten` minus `off` |
|---|---:|---|---|---|---|---|---|
| Deck | 5 | 8.445 (8.433 to 8.456, spread 0.023) | +0.144 (+0.026 to +0.596) | +0.131 (+0.012 to +0.586) | −0.029 (−0.057 to −0.016) | +0.013 (+0.006 to +0.023) | +0.160 (+0.028 to +0.612) |
| Cabin | 5 | 9.309 (9.272 to 9.337, spread 0.065) | +0.152 (+0.050 to +0.538) | +0.124 (+0.006 to +0.514) | −0.031 (−0.057 to −0.011) | +0.028 (+0.010 to +0.055) | +0.154 (+0.058 to +0.525) |
| `rect-lamps` | 5 | 7.118 (7.108 to 7.126, spread 0.017) | −0.109 (−0.113 to −0.106) | +0.004 (+0.002 to +0.005) | −0.028 (−0.037 to −0.022) | −0.112 (−0.116 to −0.109) | +0.032 (+0.025 to +0.040) |
| Crowd | 5 | 9.170 (9.147 to 9.181, spread 0.034) | +0.462 (+0.377 to +0.524) | +0.434 (+0.343 to +0.468) | −0.025 (−0.043 to −0.006) | +0.028 (−0.031 to +0.056) ‡ | +0.459 (+0.386 to +0.488) |

In the deck and the cabin, the first run of `layer` and of `ten` was 0.44 to 0.56 ms slower than the mean of their later runs. The first run of main and of `off` was not slow. Stage 1 saw the same slow first run of every side with the pass on the deck. So the next table repeats the numbers without round 1. It drops runs 1 of all four sides in every scene, for this reason only.

Rounds 2 to 5:

| Scene | Rounds | Main: mean (lowest to highest, spread) | `layer` minus main | `ten` minus main | `off` minus main | `layer` minus `ten` | `ten` minus `off` |
|---|---:|---|---|---|---|---|---|
| Deck | 4 | 8.447 (8.438 to 8.456, spread 0.018) | +0.031 (+0.026 to +0.035) | +0.017 (+0.012 to +0.020) | −0.030 (−0.057 to −0.016) | +0.013 (+0.006 to +0.023) | +0.047 (+0.028 to +0.077) |
| Cabin | 4 | 9.318 (9.300 to 9.337, spread 0.037) | +0.055 (+0.050 to +0.061) | +0.026 (+0.006 to +0.042) | −0.036 (−0.057 to −0.022) | +0.029 (+0.010 to +0.055) | +0.062 (+0.058 to +0.064) |
| `rect-lamps` | 4 | 7.120 (7.114 to 7.126, spread 0.011) | −0.109 (−0.113 to −0.106) | +0.003 (+0.002 to +0.005) | −0.029 (−0.037 to −0.022) | −0.112 (−0.116 to −0.109) | +0.032 (+0.025 to +0.040) |
| Crowd | 4 | 9.176 (9.166 to 9.181, spread 0.015) | +0.446 (+0.377 to +0.496) | +0.425 (+0.343 to +0.459) | −0.028 (−0.043 to −0.006) | +0.022 (−0.031 to +0.044) ‡ | +0.453 (+0.386 to +0.488) |

The passes. Each value is the median over the five runs of a pass's median, in ms. A cell gives main's time, then `off`, `ten` and `layer` minus main.

| Scene | Back-face pass, own time | Surface test | Depth pyramid | Reflection rays | Diffuse rays |
|---|---:|---|---|---|---|
| Deck | 0.042 | 0.119 / +0.001 / +0.012 / +0.012 | 0.150 / +0.005 / +0.005 / +0.011 | 0.577 / −0.036 / −0.035 / −0.025 | 0.549 / −0.002 / −0.002 / −0.003 |
| Cabin | 0.052 | 0.134 / +0.000 / +0.013 / +0.013 | 0.156 / +0.003 / +0.003 / +0.010 | 0.751 / −0.046 / −0.044 / −0.029 | 0.634 / −0.004 / −0.003 / +0.003 |
| `rect-lamps` | 0.043 | 0.126 / +0.000 / +0.007 / +0.007 | 0.154 / +0.008 / +0.008 / +0.012 | 0.568 / −0.039 / −0.039 / −0.071 | 0.622 / −0.003 / −0.003 / −0.044 |
| Crowd | 0.485 | 0.076 / −0.000 / +0.000 / +0.000 | 0.106 / +0.002 / +0.001 / +0.005 | 0.501 / −0.030 / −0.031 / −0.007 | 0.366 / −0.003 / −0.003 / +0.016 |

The back-face pass's own time is that of `layer`. It is the same in `ten` to 0.001 ms. The crowd's G-buffer pass takes 1.294 ms on main.

Facts of the card:

- `layer` minus main lies above or below 0 in every round of every scene. In rounds 2 to 5 it is +0.031 ms on the deck and +0.055 ms in the cabin. It is −0.109 ms in `rect-lamps` and +0.446 ms in the crowd.
- The deck's +0.031 ms is just above main's spread of 0.018 ms. Every one of its four rounds lies between +0.026 and +0.035 ms.
- `off` minus main is −0.025 to −0.036 ms in every scene, and every round is below 0. The store without the pass makes the frame slightly faster. Its reflection rays take 0.030 to 0.046 ms less than main's, and its depth pyramid takes 0.002 to 0.008 ms more.
- `ten` minus `off`, the pass alone, is +0.032 to +0.062 ms in the rooms and +0.453 ms in the crowd. The pass's own time is 0.042 to 0.052 ms in the rooms and 0.485 ms in the crowd. The surface test after it rises by 0.007 to 0.013 ms in the rooms.
- `layer` minus `ten`, the changed walks alone, is +0.013 ms on the deck and +0.029 ms in the cabin. In `rect-lamps` it is −0.112 ms, because the reflection rays and the diffuse rays both take less time. In the crowd it is +0.022 ms, and its range holds 0.

## The results on the M1 Max

The cells read as on the card. Main's spread is the slowest kept run minus the fastest.

| Scene | Rounds kept of 12 | Main: mean (lowest to highest, spread) | `layer` minus main | `ten` minus main | `off` minus main | `layer` minus `ten` | `ten` minus `off` |
|---|---:|---|---|---|---|---|---|
| Deck | 11 | 18.615 (18.577 to 18.684, spread 0.107) | +0.060 (−0.018 to +0.176) ‡ | +0.002 (−0.052 to +0.058) ‡ | +0.003 (−0.062 to +0.049) ‡ | +0.058 (+0.006 to +0.192) | −0.001 (−0.079 to +0.057) ‡ |
| Cabin | 12 | 20.291 (20.259 to 20.364, spread 0.105) | +0.140 (+0.056 to +0.213) | +0.018 (−0.072 to +0.083) ‡ | −0.010 (−0.112 to +0.110) ‡ | +0.122 (+0.078 to +0.192) | +0.028 (−0.074 to +0.060) ‡ |
| `rect-lamps` | 12 | 14.812 (14.784 to 14.846, spread 0.062) | −0.141 (−0.184 to −0.066) | +0.031 (+0.004 to +0.071) | −0.008 (−0.055 to +0.072) ‡ | −0.172 (−0.253 to −0.080) | +0.040 (−0.011 to +0.075) ‡ |
| Crowd | 9 | 15.369 (15.325 to 15.499, spread 0.174) | +1.014 (+0.876 to +1.073) | +0.862 (+0.742 to +0.978) | +0.061 (−0.071 to +0.125) ‡ | +0.152 (+0.076 to +0.221) | +0.801 (+0.713 to +0.905) |

The dropped rounds, each for a clock that did not agree within 1 MHz:

| Scene | Round | The run's mean clock |
|---|---:|---|
| Deck | 7 | `layer` 1283.18 MHz, the others 1295.89 to 1296.00 MHz |
| Crowd | 5 | `off` 1293.33 MHz |
| Crowd | 8 | main 1290.71 MHz |
| Crowd | 10 | main 1293.13 MHz, `layer` 1295.92 MHz |

Every kept run held 1295.78 to 1296.00 MHz. No run failed.

Facts of the Mac:

- `layer` minus main is +0.140 ms in the cabin, −0.141 ms in `rect-lamps` and +1.014 ms in the crowd. In those three scenes every round has the same sign.
- On the deck, `layer` minus main is +0.060 ms, and its range holds 0. It is inside main's spread of 0.107 ms.
- `off` minus main is inside the spread in every scene: each range holds 0.
- The pass alone, `ten` minus `off`, is inside the spread in the three rooms. In the crowd it is +0.801 ms, far above the spread.
- `layer` minus `ten`, the changed walks alone, has the same sign in every round of every scene. It is +0.058 ms on the deck, +0.122 ms in the cabin, −0.172 ms in `rect-lamps` and +0.152 ms in the crowd.
- The Mac has no pass times, because MoltenVK's pass times are not valid alone. No Metal System Trace ran.

## Memory at 1280 × 720

| Image | Format | Size |
|---|---|---:|
| The finest level of the depth pyramid on main | R32F | 3.52 MiB |
| The finest level under the probe | RG32F | 7.03 MiB, 3.52 MiB more |
| The back share, the probe's target of the pass | R32F (`RenderTargets.DistanceFormat`) | 3.52 MiB |
| The back share as R16F, as stage 1 sketched it | R16F | 1.76 MiB |

So the probe adds 7.03 MiB. With an R16F back share, the addition is 5.27 MiB. An R32F image at 1280 × 720 holds 921,600 texels of 4 bytes, 3,686,400 bytes.

## My reading

This section is my reading of the facts above, not a measurement.

- In the rooms, the chosen model costs little on the card: +0.03 to +0.06 ms, or 0.3 % to 0.6 % of the frame. In `rect-lamps` it saves 0.11 ms, because shorter slabs at the sill let rays pass.
- On the Mac in the rooms, it costs +0.06 to +0.14 ms or saves 0.14 ms. The changed walks carry most of that, and the pass is inside the spread.
- The dense view changes the picture. The pass draws every one-sided solid run a second time, with this frame's deformed vertices. In the crowd it costs 0.49 ms on the card, about 37 % of the G-buffer pass's 1.29 ms. On the Mac it costs 0.80 ms.
- So the pass's cost follows the vertex count, and a real scene with skinned people pays for it per frame. The crowd's 2.0 million vertices are far more than one person, about 20,000.
- The wider texel and the changed build cost nothing that the runs can show. On the card the probe's walks even save 0.03 to 0.05 ms in the reflection rays. I did not find the reason. A guess is the removed multiply by the back share at every step.
- The changed walks cost more on the Mac than on the card in the deck and the cabin: +0.058 and +0.122 ms against +0.013 and +0.029 ms. The ratio is above the rule of thumb of 2.4.

## What was not measured

- The pass's own time on the Mac: no Metal System Trace ran.
- The cause of the slow first run of `layer` and `ten` on the card in the deck and the cabin.
- The cause of the faster reflection rays of `off` and `ten` on the card.
- The card's clock per run. `clocks.csv` holds the samples of the whole job, including the idle state of 210 MHz between runs. The highest sample was 2460 to 2475 MHz.
- The scenes `station` and `demo-meshes`. The crowd replaced them as the view of dense geometry.
- An R16F back share. The probe stores it as R32F.
- Any GPU other than the RTX 2000 Ada and the M1 Max.

## The commands, the jobs and the dollars

The tools are in [tools/cost](tools/cost), copied from `/private/tmp/ter-kh4g/1b/cost/tools/`:

| File | What it does |
|---|---|
| `cloud-jobs.sh` | Sends the cloud jobs A, B and C. Run it from the worktree's root, outside the dev shell. |
| `mac-take2.sh` | One Mac take of one scene, under one exclusive hold of the GPU lock |
| `mac-take-pilot.sh` | The pilot's take, with an unbalanced order of the sides |
| `cost_report.py` | The report of each scene and side, with the kept and the dropped rounds |
| `cost_table.py` | The rows of the tables above, from the same rules |

The Mac takes, from the worktree's root, one per scene:

```bash
nix develop --command bash -c 'bash /private/tmp/ter-kh4g/1b/cost/tools/mac-take2.sh /private/tmp/ter-kh4g/1b/cost/mac <scene> 0 12'
```

The reports:

```bash
python3 -I tools/cost_report.py mac /private/tmp/ter-kh4g/1b/cost/mac
python3 -I tools/cost_report.py cloud /private/tmp/ter-kh4g/1b/cost/cloud/jobA /private/tmp/ter-kh4g/1b/cost/cloud/jobC
python3 -I tools/cost_table.py /private/tmp/ter-kh4g/1b/cost
```

The cloud jobs, with the agent name `t1-stwp-cost`, from the job log `~/.local/state/tertius/gpu-jobs/jobs.jsonl`:

| Job | Id | Result | Counted |
|---|---|---|---:|
| A and B, first try | none | Refused: the day's cap of $3.00 had $0.12 left | $0.00 |
| A, second try | none | Rejected: the push of the `layer` commit raced with job B's push (bug ticket t2-a59b) | $0.00 |
| B | `d97e1259-0bf3-4653-a225-03f086b398d4-u2` | Failed: main's tree has no `ter-kh4g-rect-lamps-timing.json` | $0.0218 |
| A | `b4c779a0-2571-4f31-9663-f577f43387e4-u2` | The deck passed. The cabin failed: main's tree has no `ter-kh4g-cabin-timing.json`. | $0.0765 |
| C | `7a17086d-c601-43a9-9883-8e118c427620-u2` | Passed: the cabin, `rect-lamps` and the crowd, 595 s | $0.1064 |

The cloud cost of this ticket is $0.2047 by the job log's estimates, of a budget of $1.00. Job C read all three scripts from the tree `script`, the branch's commit `adf373ae0`.

## The raw runs

All under `/private/tmp/ter-kh4g/1b/cost/`:

| Folder | What it holds |
|---|---|
| `mac/<scene>/<side>/run-<round>/` | `gpu-timings.json` with the clock record, `exposure.json` and `run.log` of each Mac run |
| `mac/<scene>/take-0.log` | The lock's wait, the uptime, and one line per run with its side, position, exit code, seconds and load |
| `mac/<scene>/stops.txt`, `mac/<scene>/exposure-run/` | The fixed exposure and the run of main that gave it |
| `mac/report.txt` | The output of `cost_report.py mac` |
| `mac-pilot/` | The pilot take of the deck, three rounds, not in the tables |
| `cloud/jobA/`, `cloud/jobC/` | `result.json`, and `out-<scene>/<side>/run-<round>/` with `gpu-timings.json` and `exposure.json`. `out-<scene>/` also holds `clocks.csv`, `exposure-stops.txt` and `exposure-main-auto.json`. |
| `cloud/jobB/` | `result.json` of the failed job B |
| `cloud/report.txt`, `tables.txt` | The outputs of `cost_report.py cloud` and of `cost_table.py` |

## Notes on the method

- The ticket named three sides. This record adds the mode `off`, so that the pass and the store separate.
- Stage 1 took the GPU lock once per run, and the queue between runs made its takes slow. Here one take holds the lock for all 49 runs of a scene. A run then takes 3 to 18 s, and a take of 12 rounds takes 7 to 9 minutes.
- A pilot take of the deck used the rule of stage 1's script: turn the order by the round, and reverse it in odd rounds. With four sides that rule put main only at positions 0 and 2. So the takes of the tables use a Williams square, and the pilot stays out of the tables.
- The ticket asks to drop a round whose runs did not run at the same clock. Exact equality dropped a round for 1295.92 against 1296.00 MHz, which is less than one sample in a lower state. So the rule is agreement within 1 MHz.
- The cloud ran five rounds per side and the Mac ran 12. The Mac's runs spread more, and the takes were short enough for more rounds.
