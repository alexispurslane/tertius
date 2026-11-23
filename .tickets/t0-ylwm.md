---
id: t0-ylwm
status: open
deps: []
links: []
created: 2026-11-12T13:56:23Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Bring the stand film and the climb test into the survey

Design: docs/plans/bounce-light-confidence.md, section 22. Branch t7-3e22-retained-deviation. Step 1.


## Notes

**2026-11-12T15:53:05Z**

Step 1 built on branch t7-3e22-survey-cases. The survey gets two parts with one case each, both in the orbit cabin's walk view with the bounce light alone: stand (cabin-stand, the stand film of /private/tmp/g3tk/run_dgate.sh: a still, 600 film frames from world tick 61, 10 continued end stills) and climb (cabin-climb, the seed test of /private/tmp/g3tk/look/seeds.json: 12 continued stills at ticks 300 and 306, rendered with the diffuse filter and with TERTIUS_DIFFUSE_FILTER=off, at 3.15 stops). Their scripts equal the scratch scripts exactly. The stand gives each region's excess over its end level per window of 40 draws and its settle within 5 % from the film's start and after tick 306; its verdict is acceptance 7 (r11 after tick 306 within 60 draws). The climb gives each region's peak and last share against its plateau without the filter (mean of unfiltered stills 5-11); its verdict is acceptance 11's target (r11's peak at most 10 % above its plateau), and the required check compares the candidate's share with the control's. tools/README.md, 'The measures' and 'The stand and the climb on the GPU workbench', has the commands for the Mac and the workbench. 19 new tests in tools/test_renderer_survey.py (75 in all), red first; four hand mutants of the new logic died. No GPU render and no workbench job ran: the fake-render tests cover the plumbing, and the probe patch applies to this branch (dry run).
