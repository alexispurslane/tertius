---
id: t1-lw1f
status: open
deps: []
links: []
created: 2026-11-14T22:44:54Z
type: task
priority: 3
assignee: Alexis Purslane
---
# A cloud stills job of a script with a long film passes a 500 s timeout and returns nothing

Found 2026-11-14 in stage 1 of ter-kh4g. A stills job of scripts/rect-lamps.json (a film of 240 frames, then one capture) with DEMO_ARGS='--size 1920x1080', one run of two trees after the warm-up run, ran into its timeout of 500 s and counted $0.09 with no image. tools/runpod/WORKBENCH.md shows the stills example with scripts/cabin-light.json and a timeout of 400 s, and gives no time for a script with a film. Add to WORKBENCH.md the measured time of a stills run with a film of some hundred frames at 1920 x 1080, and say that such a script needs a trimmed copy without the film's video, or a larger timeout. ab_run.sh's warm-up run also renders the whole film: a stills job needs no warm-up.


## Notes

**2026-11-15T13:18:55Z**

2026-11-15: four more cloud jobs timed out with no output, at 804, 804, 653 and 654 s (the reference experiment t1-g4p1, four columns of the cabin and rect-lamps on refs/scratch/ter-kh4g-2-ref-columns). They counted $0.51 by the job log's estimates. Every column then ran on the Mac.
