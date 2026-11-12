---
id: ter-94uo
status: closed
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

**2026-11-12T12:50:00Z**

The user chose (2026-11-12, morning): build the image with GitHub Actions, and a public image on ghcr.io.

- Secret scan before anything went public: gitleaks 8.30.1 over every commit of every ref, 3 matches, all the words "key generation" in docs/research/temporal-renderer/walk-hill-climb.md. A search for each stored secret, the gh token and key prefixes found nothing.
- https://github.com/alexispurslane/tertius is public. origin has two push URLs (knot, then GitHub); fetches stay on the knot. tools/README.md, "The GitHub mirror".
- .github/workflows/gpu-image.yml builds the image (13.5 min) from git archive of one commit and pushes ghcr.io/alexispurslane/tertius-gpu:<commit>, 3.0 GB compressed. The package is public, because the repository is. The first build failed: the Nix installer creates /nix with sudo, which ubuntu:24.04 lacks; provision.sh now creates it.
- The queue script reads the baked commit from the endpoint's image tag (one store), and `use-image <commit>` moves the endpoint to a newer public image.
- Endpoint fj0y2x968yms17: AMPERE_16 without the A4000, A4500 and RTX 4000 Ada, so RTX 2000 Ada only; workers 0 to 6, idle timeout 10 s, timeout 900 s, FlashBoot, 60 GB disk.
- First job: copy of the baked tree 1.5 s (165 MB). A build in a tree recompiles every project, also without a patch, because the restore files hold the paths under /opt/tertius: 15 to 21 s, about $0.003 a job, not worth a fix now. The first worker's image pull made the job wait 376 s; the next job waited 0.9 s.
- Deck timing through the queue (Mac trace script, three runs): median frame span 11.20 ms, 8.90 ms without capture, SM clock 2323 MHz mean. The bring-up pod measured 12.89 and 10.33 ms on older main; the thin diffuse filter took indirect diffuse from 2.71 to 1.42 ms.
- The second job counted as failed, because its own `pkill -f "lms 500"` stopped its shell; and the SDK marks a job FAILED when the handler names an error, so the script now keeps the handler's result. tools/README.md lists both as rules for a job's commands.
- Spend: the audit log holds $0.0186 for both jobs; Runpod's billing showed no serverless amount yet at 12:45 UTC.

**2026-11-12T13:25:00Z**

Review (reviewer agent): changes required, all fixed and tested. A job in flight is now a file in pending/ with its id, moved into the log in one step under the lock; a stopped script leaves it counted and the next run settles it from Runpod's final status, or at its worst case. Both caps take the larger of the log and Runpod's bill (today's, and the last 12 months; the API sums only 30 days without a window, and a start time needs an end time). One cost model: seconds plus 60 s start plus 10 s idle. SIGTERM and SIGHUP cancel like Ctrl-C. The handler enforces the job's own seconds, stops a command's background processes (output goes to a file, so a background process cannot hold a pipe), hides RUNPOD_* variables and counts the output limit in base64. Queue tests 25, handler tests 16; 14 quick mutants all killed.

Runpod's pricing page: a worker that is initializing, idle or throttled is scaled down and not billed, nor is the image pull. A running worker bills from its cold start (10 s here) through its jobs and the 10 s idle timeout.

Checks on the new image 87c7ec4b3: the commands see 0 RUNPOD_* variables, a background sleep is gone after its command, a 30 s job limit stopped `sleep 300` with the log returned. A cancel after about 30 s of `sleep 200` billed 181 s: Runpod's cancel does not stop a running handler, so the job's own limit bounds it.

After `use-image`, two stale workers of the old image took two jobs in a row and refused them (base mismatch), because the new workers waited for a GPU. `use-image` now sets the image, sets workers.max to 0 until no worker is left, and restores it; it refuses while a job is in flight. A submit during that change got 409 from /run; its pending entry has no id and counts $0.03 until it expires.

Spend in the audit log: $0.13 for nine jobs. Runpod's bill showed no serverless amount yet at 13:24 UTC.
