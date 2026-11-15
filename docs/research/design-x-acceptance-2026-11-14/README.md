# Acceptance takes of Design X, 2026-11-14

This folder holds the pictures and the scratch probe of the acceptance takes of Design X.
Design X is the design of `docs/plans/bounce-light-confidence.md`.
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
