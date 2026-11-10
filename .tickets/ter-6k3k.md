---
id: ter-6k3k
status: in_progress
deps: []
links: []
created: 2026-11-09T15:08:08Z
type: task
priority: 0
assignee: Alexis Purslane
parent: ter-8hyd
---
# Survey every scene: frame time, noise, blotches and settle time on main

Measure every scene that scripts/*.json renders on current main, at the budget's configuration, and write docs/plans/renderer-finish.md with the measures, the thresholds, the table of results and the ranked list of what fails. This is the baseline for every later ticket of the epic.


## Notes

**2026-11-10T16:55:59Z**

Baseline on main fc1c51476 (2026-11-09/10, /private/tmp/tertius-survey-baseline-20261109): all 90 timings at 1295-1296 MHz with no emulator; 22 of 90 views within 12 ms, 56 within 16.7 ms, deck (exit-deck) p95 30.8 ms; 5 of 90 stills fail (rect-lamps specks and blotches, bay-sunbeam, docking-bay, terminal, surface-terminal); 9 of 18 films fail; cabin switch 114 draws lamps off (200-draw window), 66 lamps on; bay with the sun off 48/34. Skipped on the user's order of 2026-11-10 so that steps 2 and 5 get the GPU: the lamp-hall-16 switch, the lags and the walk (the doc quotes the first survey for them), and the second pass of 8 stills that a full disk stopped (the doc quotes the first survey). The review found that the motion part compared a fixed-exposure film with auto-exposure stills; the tool is repaired, and step 10 runs the motion part again. Split by light signal (/private/tmp/tertius-split-20261109): bounce light carries rect-lamps, bay-sunbeam, rect-bay, exit-floor; reflections also fail in docking-landing and bay-sunbeam (t3-5r0z); FSR 3.1's history carries the four films with moving lamps or figures, shown by a split without FSR (t3-5jws). The .NET suites did not run: the branch changes no compiled code (tools, docs, flake's Python packages).
