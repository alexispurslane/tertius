---
id: tiunr-ax4c
status: in_progress
deps: []
links: [tiunr-py6q]
created: 2026-11-07T14:59:34Z
type: task
priority: 2
assignee: Alexis Purslane
parent: tiunr-463x
---
# Resolve slow adaptation of long reflection history to small illumination changes

Measured follow-up from tiunr-ipmk. On November 7, 2026 the user selected Design B: recheck a reference reflection ray and use changed hit/light as evidence for history rejection. In the uncapped-history experiment, a 20% rough-emitter dimming change fails to complete 90% of adjustment in 180 updates after 1,024 warm-up draws, while the matched capped control takes 19. Preserve static-noise gains and establish response to subtle source changes without confusing surface validity with reflected-illumination validity. Evidence: /private/tmp/tertius-uncapped-history-20261107/report.md. Keep precision/ray-allocation control separate from original H13 and retain failed observations.

## Acceptance Criteria

A settled design and controlled real-render tests show static noise, reference-error and illumination-response tradeoffs, including subtle dimming, moving reflected objects and large source changes; no acceptance based only on surface motion or normals.


## Notes

**2026-11-07T15:30:43Z**

User accepted uncapped history as the next baseline on November 7, 2026 and asked to discuss improved rejection after removing the cap. No rejection implementation authorized yet. Separate receiver validity from reflected-illumination validity. Candidate design directions for discussion are a direct short-versus-long light-change check or explicit reflected-source change tracking. The measured dimming failure remains the response regression case.

**2026-11-07: Reference-ray experiment authorized**

The user's “idk, try B” selects the reference-ray design from the preceding comparison, not the earlier spatial-variance Design B. Re-evaluate a matching sampled reflection path across frames so different random directions alone cannot imply scene change. Keep existing uncapped precision/allocation, ray estimator, receiver checks, H13 reconstruction, and clamp unchanged. Add an explicit opt-in experiment control and a disabled control. Parent owns native paired static/change captures, metrics, videos, and final disposition. Baseline runtime and dirty-state snapshot: /private/tmp/tertius-paired-ray-history-20261107/before/. No automatic commit, merge, or default acceptance.

**2026-11-07T17:32:11Z**

Native experiment complete, not accepted by default. Final 34/34 focused tests pass in Checked and Release. Full Rendering: Checked 2551 passed and Release 2550 passed, each with 10 existing skips. All 14 main captures pass Vulkan validation through disposal: 17248 draws, 2912 measured draws. Separate smoke adds 128 draws. Rough and smooth static outputs, history ages and unfiltered inputs match control and preceding uncapped experiment byte-for-byte across all 128 measured crop frames. Rough 20% dimming reaches persistent 90% adjustment in 118 updates versus more than 180 for control; unfinished change after 180 updates is 6.9310% versus 17.2659%. Switch-off remains 10 rough/9 smooth updates for both; native motion adds little visible benefit. One-frame evidence covers 2117/12486 plate pixels on the dimming draw and none during the following 179 measured draws. Follow-up tiunr-py6q records that coverage limit without authorizing another design. Videos and complete definitions: /private/tmp/tertius-paired-ray-history-20261107/report.md. Seven measurement tests pass. Remains opt-in, uncommitted and unmerged. Coverage/mutations, performance, Linux-driver and general-scene acceptance are not established.

**2026-11-07T18:11:01Z**

**2026-11-07: user disposition of the paired-ray check**

The user judged the result barely better and the extra ray walks a bad sign. The user selected removal of its code from the working tree. The sealed evidence under /private/tmp/tertius-paired-ray-history-20261107/ stays as the comparison control. Follow-up work continues under tiunr-py6q with the agreement test and drift sum, which needs no rays.
