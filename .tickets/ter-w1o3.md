---
id: ter-w1o3
status: closed
deps: []
links: []
created: 2026-11-12T06:45:29Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Reflection rays stop after 128 steps instead of 256: 0.55 ms on the deck

Sweep proposal (/private/tmp/tertius-sweep-20261111/addendum.md, section A10). WAITS FOR THE USER: it changes decision 41, which is the user's. The change is one number: ScreenTrace.Default gets ReflectionSteps: 128 (today 256); decision 41, the design document and ARCHITECTURE.md follow. Measured on the deck in take 5 between two baselines: frame span -0.55 ms, reflection rays 2.69 -> 2.14 ms. Image: in 57 captures of 14 more scripts the mean changes by at most 0.03 %; the deck falls by 0.04 % (16.7 % of pixels change, nearly all by one gray level of 255, 0.19 % by more). Mirror balls unchanged at 256/128/96/64; the doorway scene breaks between 96 and 64 (a cap of 64 brightens the whole floor), so 128 is proposed, not 96 (0.85 ms). Why it helps: a GPU group of 32 lanes runs until its longest walk ends. The bounce-light cap stays at 62 (a cap of 32 darkens the deck by 5 %). Builder must close: (1) captures at render sizes above 1280 x 720, which need more steps for the same ray; (2) one film with a moving camera at both caps; (3) a Linux timing on the cloud workbench.


## Notes

**2026-11-12T15:12:51Z**

The build, 2026-11-12. Alexis accepted the change of decision 41. `ScreenTrace.Default` now sets `ReflectionSteps: 128`. The acceptance record is `docs/research/frame-time-sweep-2026-11-11/acceptance-ter-w1o3/README.md`.

| Check | Result |
|---|---|
| GPU time of the deck's frame on an RTX 2000 Ada, one workbench job, five runs per cap | 8.918 ms to 8.703 ms: 0.215 ms less (2.4 %), and every run differs |
| The pass `reflection rays` in that job | 0.900 ms to 0.684 ms |
| GPU time of the deck's frame on the M1 Max, take 7 | 0.33 ms less with pass time stamps, 0.49 to 0.69 ms less without them |
| 145 captures outside the hair scenes, M1 Max | The mean changes by at most 0.067 %, and 5 captures pass 0.04 % |
| 15 captures of the hair scenes, M1 Max | The mean changes by at most 0.231 %, and enlarged crops show no fault |
| The film `exit-floor`, 180 frames, M1 Max | The mean of a frame changes by at most 0.017 % |
| 2 captures on Linux, RTX 2000 Ada | The mean changes by at most 0.018 % |
| The branch's build against the measured variant | 49 frames of 5 scripts are equal byte for byte |
| Every suite in Checked | 10,778 passed, 0 failed, 26 skipped |
| Every suite in Release | 10,765 passed, 0 failed, 26 skipped |

The three items of the ticket are closed:

1. Larger render sizes. Six scripts ran at 1920 × 1080 and at 2560 × 1440. Their 36 captures outside the hair scenes change by at most 0.067 %.
2. A film with a moving camera. Two runs at 256 steps are equal in all 180 frames. The run at 128 steps changes a frame's mean by at most 0.017 %.
3. A Linux timing. The workbench job above gives it, and two Linux stills hold.

The hair scenes pass the ticket's figure of about 0.04 %. `hair-bent-cards` changes by -0.231 % and -0.163 %, with at most 8 levels at one pixel. `lamp-hall-16-hair` changes by -0.064 %. The cap of 256 came from ticket `ter-r5hb` for these rays. The crops show no band and no other fault, so the build kept 128 steps.

The Mac take lost three of its eight traces to a fallen GPU clock, while the machine's load rose to 44. The Linux stills job hit its timeout of 600 s before its comparison step. It sent back two of the planned stills.

Tests. The options test names the new default. It failed first with 256 and passed after the change. The change adds no other test: `ScreenWalkBoundaryTests` and `ScreenRayTests` test the cap with explicit step counts. No snapshot test failed, so no reference image changed.

Mutants. The one changed code line is in Rendering, so the official Stryker run had no line. `tools/mutate.py` ran three listed mutants.

| id | place | change | result | killed by |
|---|---|---|---|---|
| C01 | src/Tertius.Rendering/ScreenTraceSetting.cs:32 | the old cap of 256 steps | killed | ScreenTraceSettingTests.TheRendererOptionsDefaultToTheDesignsTracerAndThirtyTwoWarmUpDraws |
| C02 | src/Tertius.Rendering/ScreenTraceSetting.cs:32 | one step fewer | killed | ScreenTraceSettingTests.TheRendererOptionsDefaultToTheDesignsTracerAndThirtyTwoWarmUpDraws |
| C03 | src/Tertius.Rendering/ScreenTraceSetting.cs:32 | the cap of 64 steps, which breaks the doorway floor | killed | ScreenTraceSettingTests.TheRendererOptionsDefaultToTheDesignsTracerAndThirtyTwoWarmUpDraws |

3 rows: killed 3, timeout 0, survived 0, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0.

**2026-11-12T15:57:59Z**

The merge, 2026-11-12. During the build, main gained a change of the reflection resolve (ticket `ter-xo83`). The branch took main at b1ce2701b by a merge, commit 9e8256255. The checks ran again on that code:

- A second workbench job, five runs per tree: 8.865 ms to 8.652 ms, 0.213 ms less (2.4 %), and every run differs.
- Nine Linux stills of `doorway`, `mirror-balls` and `hair-rugs`: the mean changes by at most 0.116 %, in a nearly black capture.
- Every suite in Checked: 10,780 passed, 0 failed, 26 skipped.
- Every suite in Release: 10,767 passed, 0 failed, 26 skipped.

Main then held more commits, which change the GPU job tools and no engine code. The four workbench jobs counted $0.20 in total. The acceptance record has the section "After the merge of main".
