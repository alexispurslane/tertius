---
id: ter-94uo
status: open
deps: [ter-libf]
links: []
created: 2026-11-12T01:50:17Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-vy3m
---
# Serverless image, job handler and the queue script with its caps and audit log

Build the serverless image from the bring-up's recipe, a handler that takes a source snapshot and a command list and returns logs and output files, and the one queue script that agents call. The queue script holds the key, enforces max workers, the execution timeout, $10 a day and $30 in total, and logs every job.


## Notes

**2026-11-12T03:06:25Z**

User decisions of 2026-11-11 for how the cloud workers are used:
1. The frame-time goal stays on the Mac: 12 ms on this Mac, measured on the Mac. A change counts as done only when one timed Mac run of main confirms it at the merge (one Mac run per merge, not per experiment).
2. The Linux cards are the workbench. An agent compares each variant against main inside one cloud job and reads the result as a relative gain. A gain from cutting GPU work carries over to the Mac at about 2.4 times its Linux size (RTX 2000 Ada against the M1 Max on the deck).
3. Changes that cut fixed costs (dispatches, barriers, MoltenVK encoder breaks) are timed on the Mac, because Linux does not pay those costs.
4. After each merge, the per-stage table of ter-libf runs again on both machines. A stage whose Mac-to-Linux ratio drifts by more than 30 % is timed on the Mac again.
5. Serverless starts once the user has seen a Linux still (shown 2026-11-11: /private/tmp/ter-libf/pod-deck.png), with max workers 6: at $0.00016/s that caps the spend at about $3.46 an hour, inside the $10 a day and $30 total limits.
6. Determinism is required per computer only. A checked-in hash or film is a reference for the architecture that recorded it.
Open point for the builder: origin is a tangled knot, not GitHub, so the image needs a container registry (for example Docker Hub or GHCR) and a push credential that never lands on a worker. Ask the user which registry.
