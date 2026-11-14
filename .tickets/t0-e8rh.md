---
id: t0-e8rh
status: in_progress
deps: [t0-0fhn, t0-aiim]
links: []
created: 2026-11-13T01:01:07Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# Replace the survey's block capture with the evidence census and Design X's probe knobs

Design: docs/plans/bounce-light-confidence.md (Design A with Design X's evidence), section 23. Branch t7-3e22-retained-deviation. Step 15, section 21 (the probe knobs). Remove the block-sum capture, the evidence part and command and the README section 'The block test's evidence'. Add TERTIUS_X_CENSUS, TERTIUS_X_CENSUS_EVERY, TERTIUS_X_EVIDENCE_ACTION, TERTIUS_X_CAUSES, TERTIUS_X_MOVED_ACTION, TERTIUS_X_RECENT_LIGHT_AGE and TERTIUS_X_LEAST_MOTION; keep TERTIUS_A_RETAINED_SIGMAS and TERTIUS_A_NO_CLAMP.


## Notes

**2026-11-14T15:22:57Z**

Built: the block capture, the evidence part and command, BLOCK_TEST_COMMIT and the README section went; the probe adds Design X's knobs (TERTIUS_X_CENSUS, _CENSUS_EVERY, _EVIDENCE_ACTION, _CAUSES, _MOVED_ACTION, _RECENT_LIGHT_AGE, _LEAST_MOTION; kept TERTIUS_A_MOTION_CUTS, _RETAINED_SIGMAS, _NO_CLAMP). The census reads the sample ages back every draw and counts per 8x8 block the acted level and each cause (light code 1..RecentLightAge, moved mark, own motion); the probe resolve puts own motion in bit 14 so the causes separate. Survey: parts census-still (to draw 300, acceptance 12 verdict), census-film, census-switch, census-lag, census-walk; command census. Tests first (16 red for missing census, then green, 96 pass). probe.patch applies and builds (Mac probe-build, workbench). First census still, rect-lamps on the workbench (job a843ebc1, $0.049 shared with step 14's tests): light evidence in 1.9e-2 of the bounce light's block-draws and 5.1e-3 of the reflections', no motion evidence; light cause on 7.2e-4 of bounce pixel-draws. Acceptance 12 (1e-4) fails by about 190x: the young sun-strip/jitter records of t0-qwx8 give false block light evidence in a still, which now cuts those blocks to FastCap every draw.
