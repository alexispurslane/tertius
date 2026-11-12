---
id: ter-94uo
status: in_progress
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

**2026-11-12T06:30:00Z**

Done on branch ter-94uo-serverless-queue, without Runpod and without a GPU: the job handler (handler.py, 11 tests), the queue script with its caps, reservations and audit log (gpu_job.py, 12 tests), the worker entry (worker.py), the Dockerfile with the baked commit, the Release demo and runpod==2.2.1, the configuration file (endpoint.json, empty endpoint, base and image) and tools/README.md, "GPU jobs on Runpod". Six quick mutants of the cap logic each failed a test. No image is built, no endpoint exists, and nothing is on GitHub.

Waiting for the user (asked 2026-11-12, deferred to the morning): where the image is built (a GitHub Actions mirror, Runpod's GitHub build, or a push from the Mac with gh, whose token lacks write:packages) and whether the image on ghcr.io is public or private.

Left after that choice: build and push the image, fill endpoint.json, create the endpoint, and run one real job. That job must measure two things: whether a tree's build after the patch compiles only the changed projects (the restore files hold the image's absolute project paths), and the copy time of the baked tree with its bin and obj folders.
