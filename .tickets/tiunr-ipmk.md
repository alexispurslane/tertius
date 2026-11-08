---
id: tiunr-ipmk
status: in_progress
deps: []
links: []
created: 2026-11-07T13:50:51Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-463x
---
# Measure uncapped specular history with static and changing-reflection video controls

User-authorized experiment: remove only slow specular history age limit; retain fast history, rejection and clamping. Compare static noise/accuracy and response after long warm-up to a moving reflected object and changed emitter. Deliver two short side-by-side videos of real native renderer frames, with capped controls and honest metrics. No integration or acceptance implied.

## Acceptance Criteria

Behavioral red then green for long history past half-float precision; validated capped and uncapped runs with controlled ray allocation; static metrics and motion/light-off videos; preserve identities, failures and reproducible commands.


## Notes

**2026-11-07T13:58:11Z**

Started implementation with same-checkout delegate for precise slow specular history and paired native capture harness. Parent owns saved-frame measurements and video composition. Tests of measurement separation: 7 behavioral assertion reds -> 7 greens; initial missing-result-key failures preserved separately, not counted as meaningful reds. No arbitrary build-attempt ceilings; no commits, integration or acceptance authorized.

**2026-11-07T14:59:34Z**

Experiment measured and delivered under /private/tmp/tertius-uncapped-history-20261107/. All 16 captures validated through disposal; 19,552 draws, 3,168 measured; 300/300 Checked and 300/300 Release focused tests; 7/7 measurement tests. Removing cap versus matched precise/full-rate capped control reduces rough temporal noise 68.84% and mean-reference error 2.56%; smooth 93.02% and 0.42%. Against original H13, rough noise falls 77.39% but error rises 9.55%, so no accuracy-preserving overall win. Off response removes 90% in 10 rough/9 smooth draws for both controls. Rough 20% dimming reaches 90% adjustment in 19 capped draws but not in 180 uncapped draws; 17.27% of adjustment remains. Persistent spatial speckles remain. Moving-reflection video 8 s; lighting-changes video 12 s includes rough/smooth off and rough dimming. Source/ray pairs and pre-event prefixes match. Initial red DLL provenance gap disclosed. No integration, merge, closure or default acceptance.

**2026-11-07T15:14:13Z**

User accepted uncapped history as the new default on November 7, 2026, despite the measured dimming-response and rough-reference-error tradeoffs. Scope now includes making the tested precise/full-rate uncapped mode the unset/empty default, retaining explicit historical controls, keeping rejection/clamping unchanged, and discussing rejection only after this change. New evidence: /private/tmp/tertius-uncapped-default-20261107. Existing sealed experiment remains immutable. No commit or merge requested.

**2026-11-07T15:47:16Z**

Applied the accepted precise/full-rate uncapped mode as the unset/empty default. Explicit original and precise-capped controls remain. The capture tool selects original explicitly. Eight meaningful GPU assertion reds became 192/192 focused greens. Full Rendering assembly: Checked 2,528 passed, zero failed, 10 existing skips; Release 2,527 passed, zero failed, 10 existing skips. The initial 48 candidate fixture/resource/age failures remain in XML and logs; no assertion tolerance was weakened. Standalone xUnit executes the assemblies because the normal MTP runner fails its handshake (tiunr-q0x2). Coverage did not execute; mutation tooling refused dirty source before running any mutant. Both hardening limits remain open. Two new real Demo reflection views were inspected; explicit original smoke completed 128 measured draws with zero validation messages. All eight original H13 shader modules remain byte-identical. Report and final evidence: /private/tmp/tertius-uncapped-default-20261107/report.md. Rejection and clamping are unchanged. No commit, merge, ticket closure, new quality claim, or rejection implementation.
