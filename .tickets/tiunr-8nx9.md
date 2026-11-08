---
id: tiunr-8nx9
status: in_progress
deps: []
links: []
created: 2026-11-06T15:58:55Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# Widen reflection sharing with continuous receiver-surface checks

Follow-up requested after the inspected 3x3 sharing prototype. Compare a wider single-pass neighborhood with repeated local passes before implementation. Strengthen receiver continuity checks across intervening pixels, preserve symmetric nonnegative light exchange and exact material/source/history controls, and retain actual single-frame output, temporal noise and fixed-reference observables. No reflected-object identity guarantee follows from receiver continuity. No heavy command has started. Existing cumulative evidence is 163580 KiB of the approved 163840 KiB; any new build/capture/report evidence needs a larger explicit retention bound, without deleting old evidence.

## Design

docs/plans/unity-guided-indirect-light-research.md; current prototype ticket tiunr-fjlg. Proposed minimum experiment is one 5x5 output-only pass, with the total sharing budget bounded rather than multiplied by the tap count. Alternative: two 3x3 passes with a new intermediate output. Settle this difference and the retention bound before implementation; use red-green GPU behavior checks and reuse saved reference levels.

## Acceptance Criteria

Meaningful red-green checks protect wider reach, gap/depth/normal/material boundaries, symmetric exchange, nonnegative outputs, unchanged live inputs and disabled control. Two matched smooth/rough configurations only, no new reference integration, with existing accuracy/noise metrics and actual crops. Preserve the original 3x3 prototype and all seals. No default-policy or performance acceptance.
## User decision and inspected result, 2026-11-06

Design A, one wider5x5pass, selected. User approved increasing storage as needed;
stage cap256MiB, free-disk guard5GiB. Original3x3 and all older controls remain
sealed. This ticket stays in_progress pending parent inspection/integration.

- Symmetric maximum pair exchange1/48keeps total at most one half. Stronger exact
  material/both-normal/plane tests apply to both endpoints and every segment-
  touched cell, including corner contacts. No object-identity claim follows.
- Tests-only red against frozen3x3:11intended failures/13passes. Final focused
  checks:96/96passed. One Rendering build, one tool build, two tests-only builds,
  all Release/BPR=false/no restore. No build/setup retry or full suite.
- Two matched smooth/rough cases completed64warm-up+32measured frames each. All
  unfiltered/decoded/control inputs match;576surrounding-copy pairs unchanged.
  One offline report and retained-data presentation completed, no new reference
  integration or source/history change. All old metric objects reproduce exactly.
- Compared with3x3, rough temporal noise RMS improves15.8478%and mean-error RMS
  improves1.0318%, but reflection width moves farther from reference. Smooth noise
  RMS worsens12.4438%and mean-error RMS worsens2.2856%. No post-result tuning.
- All heavy commands ended11:41:42EST, explicitly RELEASED. No owned process or
  future reservation. Default switch stays off; no production/performance verdict.

Rendering:a4c9dbfc9865416cdf1907f6758d7fe4cbeedfa749b58779bd3fc4eb932f3b44.
Tool:a431e3d2107b0fe895fd46a0437eedb5a0fff5bd02df96d7a71e4d48c6ad4b3f.
Evidence:/private/tmp/tertius-unity-noise-research-20261105/live-reference-comparison/reflection-wide-sharing-20261106-1135/README.md.
