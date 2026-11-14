# The frame-time sweep of 2026-11-11

This folder keeps the research of one sweep of the renderer's GPU frame time. The sweep measured the station deck at 1280 × 720, upscaled to 1920 × 1080, on the M1 Max. It ran on 2026-11-11 and 2026-11-12.

Read the two texts in this order:

1. [report.md](report.md) is the first report. It ranks the possible cuts of the frame time.
2. [addendum.md](addendum.md) holds what came after: the decisions, the later measurements and the ticket proposals.

## What the addendum found

- MoltenVK 1.4.2 ends the Metal encoder at every pipeline barrier outside a render pass. A patch that keeps an open compute encoder saved 0.93 ms of 24.0 ms (sections A5 and A6).
- The Mac is 2.3 times slower than an RTX 2000 Ada on this frame. The encoder breaks explain 7 % of that distance (section A4).
- A reflection walk of at most 128 steps saves 0.55 ms, and the image holds in 59 captures (section A10).
- A headless still inside the station's cabin is black with the environment map on (section A11).
- On 2026-11-12 the deck takes 19.9 ms as a window, after 24.0 ms. Section A12 holds the fresh trace and the ranked table again.
- The bloom as compute dispatches gains nothing, because its encoder breaks were cheap ones (section A12).
- The automatic exposure has several stable values in a scene with a large even surface. A still with it mixes a tested change with a step of the meter (section A13).
- A reflection walk of at most 96 steps saves 0.21 ms and changes the stills less than the accepted cap of 128 did. The bounce-light cap stays at 62 (section A13). Alexis chose the cap of 96, and ticket `t2-pw52` built it. On the code that merged, it saves 0.15 ms.
- An environment face that lights each texel once saves 0.11 ms of the mean frame on the deck. A proxy build promised 0.25 ms, because it shaded one made-up point (section A14). Ticket `t2-zcr4` built it.
- On the M1 Max, a depth clear in the middle of a rendering loses the surfaces drawn before it. The call is valid Vulkan (section A14, ticket `tz-na6d`).

## What the folder holds

| Path | Content |
|---|---|
| `pics/` | The pictures and the one film that the texts show |
| `mvk-keep-dispatch-encoder.patch` | The measured MoltenVK patch, with its measurement switches |
| `mvk-patched.nix` | The Nix expression that built the measured driver |
| `tools/` | The scripts that made the tables: the adding count of a frame, the idle time, the encoder list, the image scores. `tools/flipcmp/` runs the project's FLIP on pairs of stills. Its project file is stored as `flipcmp.csproj.txt`, so that no build of the repository reads it |
| `edits/` | One script per scratch build. Each script edits a copy of the source tree |
| `build-variant.sh`, `measure-lib.sh`, `gpu-trace-bin.sh`, `finish.sh` | The helpers that built a scratch variant and recorded a trace of it |
| `takes/` | The script, the job list and the lock log of each timed take, and the lock logs of the untimed takes |
| `trace/` | The tables of each recorded trace: the passes, the adding count and the idle time |
| `refl/` | The demo scripts of the image tests, the scripts of the untimed takes, and the image scores |
| `acceptance-ter-w1o3/` | The acceptance record of the reflection step cap of 128: timings, image comparisons and test runs |
| `acceptance-t2-pw52/` | The acceptance record of the reflection step cap of 96: timings, image comparisons and test runs |
| `acceptance-ter-2zga/` | The acceptance record of the patched MoltenVK that the flake builds: frames, traces and test runs |
| `bloom-compute-ter-lm61/` | The record of the bloom as compute dispatches, which did not merge: the patch, the timed take and the still comparison |
| `env-face-t2-zcr4/` | The record of the environment face of section A14: the job lists and lock logs of takes 14 to 19 and of the stills, and the table of every trace. Three stills show the builds with a depth clear inside a rendering |
| `acceptance-t2-zcr4/` | The acceptance record of the environment face that lights each texel once: timings, image comparisons, mutants and test runs |
| `steps-search/` | The step cap search of section A13: the job lists and lock logs of its takes, the pixel and FLIP tables of every cap, and the mottle scores |

The flake's driver patch is `native/moltenvk-keep-dispatch-encoder.patch`. It is the measured patch in the form `keep`, without the measurement switches. [tools/README.md](../../../tools/README.md) describes it in "The patched MoltenVK".

## What the folder does not hold

- The recorded traces and their exported tables. They took several gigabytes.
- The scratch builds of the variants. `build-variant.sh` and `edits/` build them again.
- The stills and the films behind the image scores, apart from `pics/`.

The texts name files in `/private/tmp/tertius-sweep-20261111`. That folder was the sweep's scratch folder, and a reboot removes it. A relative path in the texts names a file of this folder. The take scripts are the exception: they are in `takes/`.

The scripts hold absolute paths of the scratch folder and of the worktree that the sweep used. To run a script again, change those paths first.
