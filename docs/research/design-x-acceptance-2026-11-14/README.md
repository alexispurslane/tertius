# Acceptance takes of Design X, 2026-11-14

This folder holds the pictures and the scratch probe of the acceptance takes of Design X.
Design X is the design of `https://github.com/alexispurslane/tertius/wiki/bounce-light-confidence`.
The numbers of the takes are in the notes of the tickets `t0-5tyj`, `t0-f8oc`, `t0-89zi` and `t0-acxm`.

## Pictures

| File | Content |
|---|---|
| `pics/screenshot-bay.png` | The bay at tick 420 from the real renderer, main beside the branch with the fix. |
| `pics/screenshot-rect-lamps.png` | The lamp hall of `scripts/rect-lamps.json` from the real renderer, main beside the branch. |
| `pics/bay-sunbeam-floor.png` | A full-resolution crop of the bay's floor at draw 132: main, the branch, and the reference. |
| `pics/rect-lamps-ceiling.png` | A full-resolution crop of the lamp hall's ceiling at draw 132, the same three builds. |
| `pics/exit-cabin-floor.png` | A full-resolution crop of the exit cabin's floor at draw 132, the same three builds. |
| `pics/cabin-switch-full-image.png` | The cabin's lamp switch: the light over the draws, for main, the branch before the fix and the branch with the fix. |
| `pics/cabin-8-draws-after-lamps-on.png` | The cabin 8 draws after the lamps return, main beside the branch, and the branch's settled frame. |
| `pics/rect-bay-film.png` | The film `rect-bay`: frames of the film beside stills of the same states, main above the branch. |
| `pics/orbit-far-sill-row-563.png` | The far window's sill in the scene `orbit` at world tick 300, direct light alone, from the takes of `https://github.com/alexispurslane/tertius/wiki/sub-pixel-strips`: output rows 533 to 593 and columns 870 to 1049 of 1920 x 1080, six times enlarged. One lit row, row 563, from column 892 to 1030. Added on 2026-11-15 for the diagnosis of the sun's shadow leak. |

The reference is the branch with both history clamps off (`TERTIUS_A_NO_CLAMP=1`), as a mean of its last captures.

## The scratch probe

`scratch-probe-on-73f4a6548.patch` holds every scratch switch of the takes.
It applies to commit `73f4a6548`, the branch before the fix.
It contains `tools/renderer-survey/probe.patch` and these additions, which never entered `src`:

| Switch | Effect |
|---|---|
| `TERTIUS_X_LIGHT_ACTION` | What light evidence does. The value `cut` is the fix: it cuts the length and leaves the box wide. |
| `TERTIUS_X_RECENT_LIGHT_AGE` | The number of draws for which a light code counts as recent. The engine's value is 7. |
| `TERTIUS_X_DILATE` | The radius, in ray blocks, over which a block takes the light evidence of its neighbor blocks. |
| `TERTIUS_X_REGION` | The radius, in ray blocks, of the regional lag test. The value 0 turns the test off. |
| `TERTIUS_X_REGION_TOL` | The tolerance of the regional lag test, in percent of the fast mean. The default is 10. |

To build the probe, apply the patch to a scratch checkout of `73f4a6548`.
Then build `src/Tertius.Demo` in Release into `<out>/probe-bin`, as `tools/renderer-survey.py probe-build` does.

## The measuring scripts

`tools/` holds the scripts of the takes. They are tool code of one research session: `take.sh` and `common.sh` hold that session's paths.

| File | Use |
|---|---|
| `take.sh` | One survey run with the probe of a named scratch build and a list of environment switches. |
| `common.sh` | The cloud jobs of the session: the build line, the survey line and the submit call of `tools/runpod/gpu_job.py`. |
| `light.py` | The light of a still's regions as a share of the reference's long mean. |
| `daub.py` | The daub score of a still against a reference. It imports `light.py`. |
| `ratio.py`, `table.py`, `films.py` | Tables of the switch, lag and motion results of several output folders. |
| `chart.py`, `cabinpic.py`, `view.py`, `sheet.py` | The chart of a switch run, and sheets of probe captures and stills side by side. |

`light.py`, `daub.py`, `cabinpic.py` and `view.py` need numpy, so run them inside the dev shell.
