---
id: ter-b4ux
status: open
deps: []
links: []
created: 2026-11-09T22:18:27Z
type: bug
priority: 0
assignee: Alexis Purslane
parent: ter-8hyd
---
# The deck's GPU frame span rose from 13.4 ms to 30.1 ms since the build of 2026-11-05

Survey ter-6k3k, 2026-11-09, /private/tmp/tertius-survey-20261109/ab.log: scripts/temporal-upscale-timing.json at 1920x1080 output with FSR, Release, GPU clock 1296 MHz, alternating rounds in one GPU lock take. The build of 2026-11-05 (old-bin) gave a median span of 13.40 and 13.38 ms. Main gave 30.05 and 30.15 ms. The station-cabin trace on main (trace/station-cabin/passes.md) gives these times alone: diffuse filter 4.47 ms, reflection rays 3.82 ms, FSR 1.65 ms, diffuse rays 1.45 ms, specular accumulation 1.42 ms, direct light 0.77 ms. An Android emulator ran during some runs, but gpu-users.py shows that it takes no GPU time. Merges since 2026-11-05: 1560c7305 (reflected-light history rule, light-change age, tube light), 570d79288 (diffuse spatial filter), b1aee9b73 and b204dcc2e (rectangle light), d32fd30b4 (16 shadow taps), and smaller ones.

## Acceptance Criteria

Each pass's change in time between the old build and main is measured with GPU traces and attributed to the merge or commit that caused it. Each regression that comes from a plain mistake (wrong resolution, repeated work, a debug path left on, an unneeded full-screen pass) is fixed and merged. The rest are ranked with a plan to cut each one. The deck's span is measured again after the fixes.

