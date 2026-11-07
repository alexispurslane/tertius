---
id: tiunr-fjlg
status: in_progress
deps: []
links: []
created: 2026-11-06T15:06:48Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# Prototype reflection sharing without incoming capping

User-selected Design A: bounded output-only reflection sharing, meaningful GPU red-green, preserve source/history and material factors, two matched smooth/rough captures, noise and fixed-reference metrics plus actual frames. No default-policy acceptance.

## Design

docs/plans/unity-guided-indirect-light-research.md

## Inspected prototype result, 2026-11-06

Implementation and bounded experiment complete; ticket remains in_progress for
parent inspection/integration, not production acceptance. Default behavior stays
unchanged. No commit, branch, child, workflow or hardening run was started.

- Output-only3x3reflection sharing uses existing accumulated light and writes no
  source/history. Receiver material/normal/plane and reflection-direction guides
  restrict symmetric nonnegative exchange. Reflected-object identity is absent.
- Genuine red execution:4intended failures/2passes. Final focused greens:78/78.
  Earlier shader-include, test-upload and metadata-reference setup failures remain
  evidence, not behavioral reds. Actual compilations:3Rendering,3tool attempts,
  4affected-test attempts; one tool and one test build failed.
- Two new captures completed and validated: smooth/rough sharing,64warm-up plus
  32measured draws each, original native1280x720zero jitter/80x160crop. No new
  reference samples or old-control recaptures. All32unfiltered frames and decoded
  inputs per material match uncapped controls exactly.576surrounding-copy pairs
  remain unchanged. Raw equals plate plus emitter for every saved scalar.
- Against uncapped, temporal noise RMS falls29.3312%rough/25.8781%smooth;
  persistent mean-error RMS falls13.9959%/6.8368%. Crop totals change by
  -0.0021921%/-0.0002730%. Both reflections widen slightly. Smooth missing and
  extra light increase despite lower RMS error. Visible speckling remains.
- Capture and report wrappers both exited93at evidence guards after completing
  their measurements. They remain failed wrappers. Only new evidence was packed
  losslessly afterward; no rerun occurred. All7complete table bytes reconstruct
  exactly against their C#hash receipts. Final presentation/verification exited0.
- All owned heavy commands ended10:36:38EST and were explicitly RELEASED. Zero
  owned heavy process and no future reservation. The broader research goal stays
  open; motion, other scenes and performance were not evaluated.

Rendering SHA256:258b0357cc3af459422f57f56c8c0e9ea1a482067e6599d1ddf1dfed0dcf1aae.
Tool SHA256:3c8582028c15054dffdbd9f7d01c0a846175a76c06acf813098cf0c4d5ff3c93.
Evidence: /private/tmp/tertius-unity-noise-research-20261105/live-reference-comparison/reflection-sharing-20261106-1006/README.md.
