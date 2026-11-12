---
id: ter-a5ev
status: closed
deps: [ter-94uo]
links: []
created: 2026-11-12T13:36:44Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-vy3m
---
# The GPU workbench guide and its A/B tools

The queue of ter-94uo is the general optimization workbench, not a Linux check (Alexis, 2026-11-12). Write the guide for agents around that role: the A/B of a branch or a patch against main inside one cloud job, read as a relative gain with the spread of its runs; quality A/B of stills and films; what still needs the Mac's GPU lock; several jobs at once on up to 6 workers; and the cost rules and caps, with agents never holding the key. Add the small tools that the A/B needs, and measure the spread of an A/A job to say how many runs per side.

## Notes

**2026-11-12T14:00:00Z**

Done: tools/runpod/WORKBENCH.md (the guide), tools/runpod/ab_run.sh (worker: builds each tree, runs its demo in rounds whose order reverses, logs the clocks), tools/runpod/ab_report.py with 4 tests (gain against the base tree, "every run differs" or within the noise, the Mac estimate at 2.4 times, the passes that moved most). The queue script now reads the key file's path from ~/.config/tertius/runpod-key-file, so agents set nothing.

Two A/A jobs at once (the same commit as both trees, five runs per side, scripts/temporal-upscale-timing.json): medians 8.895 against 8.890 ms and 8.863 against 8.832 ms, so -0.1 % and -0.3 %; the spread of five runs 0.07 to 0.14 ms, with one slow first run of 9.37 ms; pass medians within 0.002 ms; 0.4 % between the two workers. Each job about 150 s, $0.036. A still A/B of cabin-light gave bit-identical stills of the same commit on one worker; a 1080p still is 0.8 to 2.3 MB.

The endpoint runs main's image 5138be7a4 since 13:40 UTC.
