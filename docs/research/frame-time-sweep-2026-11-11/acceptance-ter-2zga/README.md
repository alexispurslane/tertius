# The acceptance of the patched MoltenVK (ticket ter-2zga)

This folder records the acceptance of the MoltenVK that the flake builds from `native/moltenvk.nix`. [tools/README.md](../../../../tools/README.md) describes the driver in "The patched MoltenVK". Every run was on the M1 Max on 2026-11-12, in the dev shell of branch `ter-2zga-patched-moltenvk`.

The tables name two drivers:

- The patched driver is the shell's default driver: `/nix/store/jxv1cgaay6396aqwbcvkikamsv66vvkh-MoltenVK-1.4.2`.
- The stock driver is the nixpkgs build without the two patches: `/nix/store/i15qwcipchfkg00vf1bjd89dyj42qc5r-MoltenVK-1.4.2`. The shell's variable `TERTIUS_STOCK_MOLTENVK_ICD` names its ICD manifest.

The runs used four states of the code:

- "Before the merge" is commit `527cd445a`. Its engine code is the code of main at `58ce67f66`.
- "After the first merge" is commit `32f741740`. Its engine code is the code of main at `7eed8d130`.
- "After the second merge" is commit `f831de851`. Its engine code is the code of main at `75ecde376`. That code zeroes FSR's images in one compute dispatch, not in 12 clears (ticket ter-nstr).
- "After the third merge" is commit `05eed98ac`. Its engine code is the code of main at `5330c7411`. That code writes the light at a ray's hit into one image (ticket ter-67y3).

The branch changes no engine code. Each merge of main brought main's later engine changes under the patched driver, so the main checks ran again.

## The result

| Check | Before the merge | After the first merge | After the second merge | After the third merge |
|---|---|---|---|---|
| Frames of the deck, patched against stock | 29 frames, 0 differ | 29 frames, 0 differ | 29 frames, 0 differ | 29 frames, 0 differ |
| Frames of 24 capture scripts and the low view, patched against stock | Not run | 139 frames, 0 differ | 139 frames, 0 differ | 139 frames, 0 differ |
| Frames under Metal's API validation, patched against patched | 62 frames, 0 differ | 113 frames, 0 differ | 80 frames, 0 differ | 80 frames, 0 differ |
| GPU frame span of the deck, stock to patched | 24.10 to 23.22 ms | 22.02 to 21.09 ms | 21.82 to 20.81 ms | 21.56 to 20.71 ms |
| Metal encoders of one deck frame, stock to patched | 116 to 79 | 117 to 79 | 105 to 66 | 105 to 66 |
| Idle time inside one deck frame, stock to patched | 2.23 to 1.40 ms | 2.25 to 1.40 ms | 2.06 to 1.22 ms | 2.08 to 1.26 ms |
| Every test suite in Checked | 10,757 passed, 0 failed, 26 skipped | 10,761 passed, 0 failed, 26 skipped | 10,770 passed, 0 failed, 26 skipped | The touched test classes only: 276 passed, 0 failed |
| Tests under Metal's API validation | Rendering and Host tests: 5,119 passed, 0 failed, 18 skipped | Not run | FSR's test classes: 67 passed, 0 failed, 5 skipped | The touched test classes: 276 passed, 0 failed |
| Every test suite in Release | 10,744 passed, 0 failed, 26 skipped | Not run | 10,757 passed, 0 failed, 26 skipped | Not run |

The patch saves 0.89 ms of the deck's frame before the merge. It saves 0.93 ms after the first merge, 1.01 ms after the second and 0.85 ms after the third. Section A5 of [the addendum](../addendum.md) measured 0.93 ms, 116 to 79 encoders, and 2.24 to 1.40 ms of idle time.

The Linux dev shells did not change. The derivations of `devShells.x86_64-linux.default` and `devShells.aarch64-linux.default` equal main's at `7eed8d130`, at `75ecde376` and at `5330c7411`:

- `x86_64-linux`: `/nix/store/jv1plr8m4jpq497q6jxlf60s7zfcd1qv-nix-shell.drv`
- `aarch64-linux`: `/nix/store/w1zb0sxhy1kasih6y0i8r69myjvjg711-nix-shell.drv`

## The frames

A frame check runs one demo script twice, headless, and compares the written files byte for byte. The two runs differ only in the driver or in one variable. The files `stills-*.jobs` hold the runs, and `../refl/stills3.sh` runs a job list. `../tools/framecmp.py` made the table below.

Before the merge, the binary was the build of take 3, which fixes the deck's exposure:

| Script | Frames | Differ under stock | Differ under the stock-behavior switch | Differ under validation |
|---|---:|---:|---:|---:|
| `../refl/deck.json` (the deck) | 29 | 0 | 0 | 0 |
| `../refl/views2.json` (the low view of the scene `reflections`) | 33 | 0 | Not run | 0 |

The low view is a weak check in that table. The deck's fixed exposure is too bright for the scene `reflections`, so most of each frame is white.

After the first merge, the same two scripts ran again with the merged code and the fixed exposure. The deck's 29 frames and the low view's 33 frames did not differ under stock or under validation.

The wide check then used the merged code without an edit, so each scene has its own exposure. It ran the low view and the 24 scripts of `../refl/capscripts/`. "Other files" are the entity dumps and the exposure record of a run. The check leaves out the GPU timings, because they differ from run to run.

| Script | PNG files | PNG files that differ | Other files | Other files that differ |
|---|---:|---:|---:|---:|
| `airlock` | 4 | 0 | 12 | 0 |
| `bounce-red-wall` | 1 | 0 | 1 | 0 |
| `cabin-light` | 3 | 0 | 1 | 0 |
| `deep-yard` | 2 | 0 | 8 | 0 |
| `demo-meshes` | 5 | 0 | 2 | 0 |
| `demo-shuttle` | 3 | 0 | 2 | 0 |
| `demo-surface` | 7 | 0 | 6 | 0 |
| `doorway` | 4 | 0 | 4 | 0 |
| `low` (`views2.json`) | 33 | 0 | 1 | 0 |
| `mars-air` | 6 | 0 | 1 | 0 |
| `mirror-balls` | 3 | 0 | 1 | 0 |
| `pbr-spheres` | 2 | 0 | 1 | 0 |
| `reflections-stills` | 3 | 0 | 1 | 0 |
| `rigs` | 5 | 0 | 1 | 0 |
| `spacewalk` | 1 | 0 | 8 | 0 |
| `spin-station` | 10 | 0 | 7 | 0 |
| `station-crate` | 12 | 0 | 1 | 0 |
| `station` | 7 | 0 | 8 | 0 |
| `station-nocrate` | 12 | 0 | 1 | 0 |
| `surface-yard` | 5 | 0 | 6 | 0 |
| `ter-7tnv-floor` | 2 | 0 | 2 | 0 |
| `ter-7tnv-terminal` | 2 | 0 | 2 | 0 |
| `throw` | 2 | 0 | 8 | 0 |
| `walk` | 2 | 0 | 7 | 0 |
| `windows` | 3 | 0 | 5 | 0 |
| Sum | 139 | 0 | 97 | 0 |

Four scripts of the wide check also ran under `MTL_DEBUG_LAYER=1`, which turns on Metal's API validation: `low`, `doorway`, `demo-surface` and `station`. Their 51 frames equal the frames without validation. Each log holds the line "Metal API Validation Enabled" and no assertion.

After the second merge, the wide check ran again (`stills-wide-3a.jobs`, `stills-wide-3b.jobs`), with the deck as one more script. The deck's run uses the fixed exposure. All 168 PNG files and all 98 other files equal those of the stock driver. Five scripts also ran under `MTL_DEBUG_LAYER=1`: `deck`, `low`, `doorway`, `demo-surface` and `station`. Their 80 frames equal the frames without validation, and no log holds an assertion.

The 139 frames of the wide check after the second merge also equal those after the first merge. So main's change of FSR's clear step changed no frame of these scripts.

After the third merge, the same wide check ran once more (`stills-wide-4a.jobs`, `stills-wide-4b.jobs`). All 168 PNG files and all 98 other files equal those of the stock driver. The 80 frames of the five scripts under `MTL_DEBUG_LAYER=1` equal the frames without validation, and no log holds an assertion.

Main's hit light image changed 153 of the 168 frames against the second merge. So the check sees a change of the frame, and both drivers give the same new frames.

Three frames of the patched driver after the third merge are in `pics/`. Each equals the stock driver's frame byte for byte:

- `pics/deck-patched.png`: the station deck, the view that the traces time.
- `pics/low-patched.png`: the low view of the scene `reflections`, with a red box, a tube light and a blue ball above a glossy floor.
- `pics/terminal-patched.png`: the terminal of the script `demo-surface`, a room that a bright doorway lights.

## The traces

Each trace records 120 frames of `scripts/temporal-upscale-timing.json`, the station deck at 1280 × 720, upscaled to 1920 × 1080. The pass time stamps are off, as in a window. `take6.sh` ran each job list in one exclusive take of the GPU lock.

The columns:

- "Timer" is the median GPU frame span that the renderer's own timer reports, in milliseconds.
- "Span" is the median time from a frame's first GPU interval to its last, in milliseconds. `../tools/idle.py` reads it from the trace.
- "Idle" is the median part of the span in which no GPU interval of the demo is open, in milliseconds.
- "Encoders" is the count of Metal encoders of one frame, with the compute encoders in brackets.

Before the merge. The binary is take 3's build, or the branch's own build in the two rows that say so:

| Take | Trace | Driver | Timer | Span | Idle | Encoders |
|---|---|---|---:|---:|---:|---:|
| 6a | `deck-acc-stock` | Stock | 23.95 | 24.06 | 2.23 | 116 (47) |
| 6a | `deck-acc-keep` | Patched | 23.12 | 23.23 | 1.39 | 79 (10) |
| 6a | `deck-acc-keep-b` | Patched | 23.09 | 23.19 | 1.39 | 79 (10) |
| 6a | `deck-acc-br-stock` | Stock, branch build | 24.10 | 24.21 | 2.23 | 116 (47) |
| 6a | `deck-acc-br-keep` | Patched, branch build | 23.17 | 23.29 | 1.39 | 79 (10) |
| 6b | `deck-acc-switch-2` | Patched with `TERTIUS_MOLTENVK_STOCK_BARRIERS=1` | 24.07 | 24.18 | 2.24 | 116 (47) |
| 6b | `deck-acc-stock-c` | Stock | 24.04 | 24.15 | 2.24 | 116 (47) |
| 6b | `deck-acc-keep-c` | Patched | 23.10 | 23.22 | 1.42 | 79 (10) |
| 6b | `deck-acc-switch-3` | Patched with `TERTIUS_MOLTENVK_STOCK_BARRIERS=1` | 24.05 | 24.15 | 2.23 | 116 (47) |

After the first merge. The binary is the merged branch's Release build with the deck's fixed exposure:

| Take | Trace | Driver | Timer | Span | Idle | Encoders |
|---|---|---|---:|---:|---:|---:|
| 6c | `deck-acc-m-stock` | Stock | 21.92 | 22.03 | 2.25 | 117 (48) |
| 6c | `deck-acc-m-keep` | Patched | 21.01 | 21.09 | 1.39 | 79 (10) |
| 6d | `deck-acc-m-stock-c` | Stock | 21.89 | 22.01 | 2.25 | 117 (48) |
| 6d | `deck-acc-m-keep-c` | Patched | 21.15 | 21.13 | 1.43 | 79 (10) |
| 6d | `deck-acc-m-switch` | Patched with `TERTIUS_MOLTENVK_STOCK_BARRIERS=1` | 22.04 | 22.15 | 2.26 | 117 (48) |
| 6d | `deck-acc-m-keep-d` | Patched | 20.92 | 21.04 | 1.39 | 79 (10) |

After the second merge. The binary is that code's Release build with the deck's fixed exposure:

| Take | Trace | Driver | Timer | Span | Idle | Encoders |
|---|---|---|---:|---:|---:|---:|
| 6e | `deck-acc-n-stock` | Stock | 21.67 | 21.78 | 2.06 | 105 (48) |
| 6e | `deck-acc-n-keep` | Patched | 20.71 | 20.81 | 1.22 | 66 (9) |
| 6e | `deck-acc-n-switch` | Patched with `TERTIUS_MOLTENVK_STOCK_BARRIERS=1` | 21.88 | 21.99 | 2.10 | 105 (48) |
| 6e | `deck-acc-n-stock-b` | Stock | 21.74 | 21.85 | 2.07 | 105 (48) |
| 6e | `deck-acc-n-keep-b` | Patched | 20.71 | 20.81 | 1.21 | 66 (9) |

After the third merge. The binary is that code's Release build with the deck's fixed exposure:

| Take | Trace | Driver | Timer | Span | Idle | Encoders |
|---|---|---|---:|---:|---:|---:|
| 6f | `deck-acc-o-stock` | Stock | 21.46 | 21.58 | 2.10 | 105 (48) |
| 6f | `deck-acc-o-keep` | Patched | 20.63 | 20.72 | 1.25 | 66 (9) |
| 6f | `deck-acc-o-switch` | Patched with `TERTIUS_MOLTENVK_STOCK_BARRIERS=1` | 21.51 | 21.61 | 2.11 | 105 (48) |
| 6f | `deck-acc-o-stock-b` | Stock | 21.43 | 21.54 | 2.07 | 105 (48) |
| 6f | `deck-acc-o-keep-b` | Patched | 20.60 | 20.69 | 1.27 | 66 (9) |

The table of "The result" gives the mean of the "Span" column for each driver. It leaves out the rows of the stock-behavior switch and the two rows of the branch build.

Main's engine changes between `58ce67f66` and `7eed8d130` made the deck's frame 2.1 ms shorter and added one compute encoder under the stock driver. The patch's gain stayed the same.

Main's change of FSR's clear step removed 12 render encoders of a frame. The patch then leaves 9 compute encoders, not 10. The gain of 1.01 ms is 0.08 ms more than after the first merge. That difference is near the spread between two runs of one driver.

After the third merge the gain is 0.85 ms with the same encoder counts. Over the four states of the code, the gain is between 0.85 and 1.01 ms.

Four traces are not in the tables:

- `deck-acc-switch` and `deck-acc-stock-b` of take 6a, and `deck-acc-m-stock-b` of take 6c. The GPU clock dropped during each one: its 10th percentile was 1,227, 1,064 and 980 MHz against 1,290 MHz. Take 6b and take 6d repeated them.
- `deck-acc-m-keep-b` of take 6c. The recorder `xctrace` did not end, and the trace is not readable.

Some frames of `deck-acc-m-keep-c` and `deck-acc-m-switch` were slow at a full GPU clock, so their mean spans are 21.67 and 23.59 ms. The medians hold.

`../trace/<trace>/idle.json` holds the numbers of each trace.

## The tests

Each run held one shared take of the GPU lock, with the patched driver as the shell's default. `tests/acc-tests.sh` starts a run, and `tests/<name>.lock.log` holds its output.

| Run | Code | Command | Passed | Failed | Skipped | Log |
|---|---|---|---:|---:|---:|---|
| Every suite in Checked | Before the merge | `dotnet test --solution Tertius.slnx -c Checked` | 10,757 | 0 | 26 | `checked.lock.log` |
| Rendering tests under `MTL_DEBUG_LAYER=1`, classes A to E | Before the merge | `tests/acc-v1.cmd.sh` | 1,055 | 0 | 0 | `v1.lock.log` |
| Rendering tests under `MTL_DEBUG_LAYER=1`, classes F to P | Before the merge | `tests/acc-v2.cmd.sh` | 919 | 0 | 9 | `v2.lock.log` |
| Rendering tests under `MTL_DEBUG_LAYER=1`, every other class | Before the merge | `tests/acc-v3.cmd.sh` | 1,098 | 0 | 1 | `v3.lock.log` |
| Host tests under `MTL_DEBUG_LAYER=1` | Before the merge | `tests/acc-vhost.cmd.sh` | 2,047 | 0 | 8 | `vhost.lock.log` |
| Every suite in Release | Before the merge | `tests/acc-release.cmd.sh` | 10,744 | 0 | 26 | `release.lock.log` |
| Every suite in Checked | After the first merge | `tests/acc-checked2.cmd.sh` | 10,761 | 0 | 26 | `checked2.lock.log` |
| Every suite in Checked | After the second merge | `tests/acc-checked3.cmd.sh` | 10,770 | 0 | 26 | `checked3.lock.log` |
| FSR's Rendering test classes under `MTL_DEBUG_LAYER=1` | After the second merge | `tests/acc-vfsr3.cmd.sh` | 67 | 0 | 5 | `vfsr3.lock.log` |
| Every suite in Release | After the second merge | `tests/acc-release3.cmd.sh` | 10,757 | 0 | 26 | `release3.lock.log` |
| The touched Rendering test classes in Checked | After the third merge | `tests/acc-t4.cmd.sh` | 276 | 0 | 0 | `t4.lock.log` |
| The same classes under `MTL_DEBUG_LAYER=1` | After the third merge | `tests/acc-vt4.cmd.sh` | 276 | 0 | 0 | `vt4.lock.log` |

The five skipped tests of FSR's classes measure FSR's GPU time, and they run only with `TERTIUS_FSR_TIMING=1`.

The touched test classes of the third merge are the classes whose names start with `HitLight`, `ScreenTrace`, `DescriptorLayouts` or `Reflection`. The third merge changed the screen trace and added the tests of the hit light image.

The three parts of the Rendering tests under validation sum to 3,082 tests. That is the count of the Rendering tests in the full Checked run before the merge. The parts exist because a shared take holds the lock for about 20 minutes at most.

## The merge into main

Alexis decided the merge on 2026-11-12. Before it, the branch took main twice more, and the checks of `tools/README.md`, "The patched MoltenVK", ran on the last state.

| Commit | Main inside it | Main's change | Checks with the patched driver |
|---|---|---|---|
| `a9be99864` | `5330c7411` | None since the third merge | Every suite in Checked: 10,778 passed, 0 failed, 26 skipped (`tests/checked5.lock.log`). 80 frames of 5 scripts equal the stock driver's |
| `bc53e6d70` | `ad095e599` | The light-age cut of the reflection history | Every suite in Checked: 10,778 passed, 0 failed, 26 skipped (`tests/checked6.lock.log`). Every suite in Release: 10,765 passed, 0 failed, 26 skipped (`tests/release6.lock.log`). 80 frames of 5 scripts equal the stock driver's (`stills-wide-6.jobs`) |

The five scripts of the frame comparison are the deck, the low view, `doorway`, `demo-surface` and `station`.

Main gained two more commits before the merge. They change one ticket file and no code.

One take of that morning ran a wrong job list. Two queued takes shared one list file, `refl/stills3.jobs`, so the take of `stills-wide-6.jobs` first rendered the images of another ticket. `acc-wide.sh` now passes each take its own list through `../refl/stills4.sh`, and the frame comparison ran again. The log `stills-wide-6.lock.log` is the log of the second run.

## What the acceptance does not show

- No test asserts on the effect of the patch, because the engine does not depend on it. The encoder count of a trace shows the effect.
- A wrong fence makes two encoders run in the wrong order only sometimes. Equal frames in every run lower that risk. They do not remove it.
- The Rendering and Host tests ran under Metal's API validation in full before the merge only. After the second merge, only FSR's test classes ran under it, because that merge changed FSR's code.
- After the third merge, only the touched test classes ran. The full suites ran again on the last state before the merge into main ("The merge into main").
- Every run was on one machine, the M1 Max. The patch changes only MoltenVK, so a Linux driver is not a part of this acceptance.
