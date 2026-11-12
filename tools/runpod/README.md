# GPU jobs on Runpod: how the queue works

An agent runs a GPU job, such as a build and a timing run of the demo, on a Runpod serverless endpoint. The agent calls one script, `tools/runpod/gpu_job.py`, and the script keeps the spending within the caps. [WORKBENCH.md](WORKBENCH.md) is the guide for agents: the A/B against main, the quality A/B, what still needs the Mac, and the costs. This file describes how the queue works. Tickets ter-94uo, ter-a5ev and ter-k7fd made the parts.

| Part | What it does |
|---|---|
| `gpu_job.py` | It runs on the Mac. It scans and publishes the trees' commits and picks the endpoint. It checks the caps, sends the job and writes the audit log. |
| `handler.py` | It runs on the worker. It fetches each tree's commit, runs the commands in the dev shell and collects the output files. |
| `worker.py` | The image's entry. The Runpod SDK gives it each job, and it calls the handler in a fresh work folder. |
| `Dockerfile`, `provision.sh` | The image: the dev shell, the source of one commit with its Release demo built, and the worker. |
| `.github/workflows/gpu-image.yml` | It builds the image on GitHub and pushes it to `ghcr.io/alexispurslane/tertius-gpu:<commit>`. |
| `endpoint.json` | The configuration of the script: the image repository, the public repository, the price, the caps and the time limits. |
| `ab_run.sh`, `ab_report.py` | The A/B of the workbench: the runs of each tree on the worker, and their report on the Mac. |

## Run a job

If `TERTIUS_RUNPOD_KEY_FILE` is set, the script reads the key's file from that path. Otherwise it reads the path from `~/.config/tertius/runpod-key-file`. It reads the key for each request and never prints it. Run the job from the repository:

```bash
python3 -I tools/runpod/gpu_job.py submit --agent <your name> \
    --tree main=main --tree branch=HEAD \
    --run 'cd branch && dotnet run --project src/Tertius.Demo -c Release -- --script scripts/demo-exit-deck-cuts.json --headless --out out' \
    --output 'branch/out/*.json' --out <folder>
```

- Each `--tree NAME=REF` becomes the folder `NAME` in the job's work folder. It holds the files of the commit of `REF`, without `docs/` and `.tickets/`. Commit your change first. The commit need not be on GitHub, because the script publishes it.
- Each `--run` command runs in the work folder, in the image's dev shell. The first command that fails ends the job.
- `--timeout` gives the seconds of all commands together, a positive number. It is at most `job_seconds` of the configuration, 840 s. The handler stops the commands at that limit and still sends back the log and the files.
- Run the script in the background, because a job can wait minutes for a worker. If the script gets Ctrl-C, SIGTERM or SIGHUP, it cancels the job and waits for its final status. A cancel does not stop a command that already runs, so the log counts a cancelled job at its worst case.
- `<folder>` gets the files of the `--output` globs and `result.json`. That file holds each command's exit code and time, the end of the log, and the files past the size limit of 8 MB.

If every command succeeded, the script exits with 0.

## How a commit reaches the worker

A job names its trees' commits, not their files. Before the job, the script does these steps, in this order:

1. It finds the commit of each tree's ref.
2. If the trees have different dev shells (next section), it refuses the job.
3. It asks GitHub for its branches and tags with `git ls-remote`. Then it scans for secrets each commit of the job's history that none of them holds. The script asks GitHub itself, because origin's tracking refs also move when a push reaches only the knot.
   - The scan is gitleaks with its default rules and `--redact`, from the repository's pinned nixpkgs (8.30.1 on 2026-11-12). It also reads the changes of merge commits. A commit's own `.gitleaks.toml` or `gitleaks:allow` comment cannot silence it. The scan reads the repository's git folder, not a working tree, so a `.gitleaksignore` in the working tree cannot silence it either.
   - A second search looks for the text of the Runpod key.
   - A finding refuses the job, and the script publishes nothing. The log names the rule, the file and the commit, never the secret.
4. It picks the image, checks the caps, picks or creates the endpoint, and records the job as in flight.
5. It pushes each commit to a hidden ref of the public repository, `refs/gpu-jobs/<UTC day>/<agent>/<commit>`, over SSH with the Mac's own key. A hidden ref is not a branch or a tag, so no clone fetches it. The push skips the repository's pre-push hook.
6. It sends the job. Its input holds the protocol, the repository's URL, and each tree's commit and hidden ref.
7. It deletes the hidden refs that are older than the three days before today. So the refs of four UTC days stay: today's and those of the three days before.

The worker makes each tree as a sparse, shallow, partial clone. Such a clone fetches the objects of one commit only, without `docs/` and `.tickets/`. It fetches the hidden ref and makes sure that the ref names the job's commit. If the ref names another commit, the job fails before any command runs. A tree's fetch took about 2 s on 2026-11-12.

The job's input no longer holds the trees' files. So its size limit of 10 MB no longer limits how far a tree can be from the image's commit.

## The dev shell, the image and the endpoint

A commit's dev shell is the content of `flake.nix`, `flake.lock` and `native/`. The image bakes one dev shell, so a job runs only in an image of its commits' dev shell. A job also needs an image whose handler speaks the script's protocol: the number `PROTOCOL` in `handler.py` and `gpu_job.py`. The script reads both from each image's commit.

Before each job, the script lists the images of the registry. Of the images whose commit has the job's dev shell and whose handler speaks the protocol, it takes one in this order:

1. The branch's own image: the newest one that the job's commits descend from and that main does not hold. A tag `gpu-image-*` on the branch builds it.
2. Otherwise, the newest image of main. So the jobs of all branches without their own image share one endpoint.

An image of another branch never runs the job. "Main" is `refs/remotes/origin/main`, main as the repository last fetched it from GitHub. Runpod's list of endpoints is the one store of the live endpoint: the script finds an endpoint by its image, and no file holds its id. The script's own endpoints are those named `tertius-gpu-<12 hex characters>`, and the first endpoint, with the bare name `tertius-gpu`. It deletes no other endpoint.

Runpod limits the account to 10 workers, summed over the maximums of all its endpoints. Under the lock, the script routes each job in three steps:

1. It deletes each endpoint of its own that does not run the chosen image, that no job in flight names, and on which no worker runs. An old endpoint thus goes after its last job, and no job of it is cut. Its workers return to the quota.
2. If no endpoint runs the chosen image, the script creates one with the workers that the quota leaves, at most 6. Example: 4 beside a busy old endpoint of 6. If the quota leaves none, the job runs on the endpoint of the best fitting image that has one. Only if no such endpoint exists, the script refuses the job.
3. If the quota leaves room, the script raises the live endpoint to 6 workers.

The script creates an endpoint as `tertius-gpu-<12 characters of the commit>`, with a 900 s timeout per job, FlashBoot and the 16 GB pool `AMPERE_16` without its other three cards. So every job runs on an RTX 2000 Ada. `gpu_job.py endpoint-request <commit>` prints that request and sends nothing.

If any endpoint of the script has a maximum of 0 workers, the script refuses every job and deletes nothing. When the spending nears its limit, the orchestrator's watchdog stops all endpoints that way. The watchdog does not take the script's lock. So the script reads the list of endpoints again before each deletion, after a creation and before it raises an endpoint. If one of those reads shows a stop, the script deletes its new endpoint again and refuses the job.

This narrows the race with the watchdog but does not close it. A watchdog that listed the endpoints before the script created one does not stop the new one in that round. A stop that lands between the script's last read and its raise of an endpoint is undone until the watchdog's next round. That round comes 5 minutes later, and the caps stay a separate guard. If the orchestrator restarts the endpoints after a stop, the sum of their maximums must stay within 10.

So a new image replaces the old endpoint without a drain: new jobs go to the new endpoint, and the jobs of the old one finish there. The caps and the audit log count the jobs of every endpoint, because each job records its own endpoint.

## A new dev shell

If a job's commits change the dev shell and no image has it, the script refuses the job and names the missing image, `ghcr.io/alexispurslane/tertius-gpu:<commit>`. Build it first. The workflow `gpu-image` on GitHub builds the image from the files of one commit, with `git archive`, in about 14 minutes. Three events start it:

- A push to main that changes `flake.nix`, `flake.lock`, `native/`, the workflow, or the image's own files: `Dockerfile`, `provision.sh`, `handler.py` and `worker.py`.
- A push of a tag `gpu-image-<anything>` to GitHub, for any commit: `git push --no-verify git@github.com:alexispurslane/tertius.git <commit>:refs/tags/gpu-image-<name>`.
- A manual run: `gh workflow run gpu-image.yml --repo alexispurslane/tertius`.

When the build passed, the next job of that dev shell creates the new endpoint itself.

## The caps

The caps are $10 per UTC day and $30 in total, for all agents together. Before the script sends a job, it adds the job's worst case to two amounts:

1. The spending: the larger of the audit log's sum and Runpod's bill. The bill (`GET /v2/billing`) holds the account's other spending, such as pods, but it lags the jobs by minutes or more. The daily cap uses today's sums, and the total cap uses the sums of the last 12 months, the account's whole life.
2. The worst cases of the jobs in flight, on every endpoint.

If either sum passes its cap, the script refuses the job, sends nothing and logs the refusal. A file lock spans the check and the record of the new job in flight. So two agents cannot both take the last of a cap.

One cost model serves the worst case and the logged cost: the seconds, plus 60 s for a worker's start, plus 10 s of idle time after the job, times the price per second. For the worst case, the seconds are the execution timeout: the commands' time plus 60 s for the handler, and at most the endpoint's timeout. For the logged cost, they are Runpod's execution time. A job on a warm worker bills no start, so the log overcounts rather than undercounts.

Runpod bills a worker only while it runs: its cold start, 10 s here with FlashBoot, its jobs and its 10 s of idle time. It does not bill the image pull, or a worker that is initializing, idle or throttled ([pricing](https://docs.runpod.io/serverless/pricing)).

## Jobs in flight and the audit log

The state folder `~/.local/state/tertius/gpu-jobs/` holds two stores:

- `pending/` holds one file per job in flight, with its endpoint, its image, its worst case and, after Runpod accepts the job, its id.
- `jobs.jsonl` holds one JSON line per settled job, a refusal too. The line holds the time, the agent, the endpoint, the image and the commands. It also holds the trees with their refs, commits and hidden refs, the job's id, the GPU seconds, the cost, the result and the exit codes.

When a job ends, the script moves it from `pending/` to the log in one step under the lock. If the script stops first, for example from a lost network or a kill, the job stays in `pending/` and counts at its worst case. The next run of the script asks Runpod for that job's final status and settles it. While a run waits for its job, it holds the job's owner lock, `pending/<name>.owner`. Another run does not settle such a job, so the job's endpoint stays and its own run gets its result. The system releases the lock when the run dies. A job that Runpod no longer knows costs its worst case. A job counts until its queue time and its timeout pass. If Runpod reports no end by then, the log records it as `lost` at its worst case. A failed push of the commits makes no job, and neither does an HTTP error of `/run`. The log records such a job as `rejected` at no cost. A failed git command, registry call or Runpod call before the job is recorded refuses the job, and the log says why.

## Rules for a job's commands

- A command runs as `bash -c '<command>'` inside the dev shell. Its text is in that shell's command line. So `pkill -f <pattern>` with a pattern from the command stops the command itself.
- When a command ends, every process that it started in the background stops too, such as a clock log. Start and use such a process in the same command. This also stops dotnet's build servers, so join two builds into one command with `&&` to keep the compiler warm.
- The commands see none of the worker's `RUNPOD_*` variables.
- If a command fails, the Runpod SDK marks the whole job FAILED. The audit log and `result.json` keep the handler's own result (`failed`, `timed out` or `refused`), and `result.json` keeps Runpod's status as `runpodStatus`.
- The worker's image has no `bc`. Use `date +%s%N` and shell arithmetic for a time.

## The tests

The test files run on the Mac without Runpod and without a GPU. Fakes stand in for Runpod, the registry, GitHub and the scan, and a local bare repository stands in for GitHub in the handler's tests.

```bash
python3 -I tools/runpod/test_handler.py
python3 -I tools/runpod/test_gpu_job.py
python3 -I tools/runpod/test_ab_report.py
```

## What the first jobs measured

The first jobs ran on 2026-11-12. A worker had an RTX 2000 Ada, an AMD EPYC 7352 with 48 threads, 251 GB of memory and 60 GB of disk. The image is 3.0 GB compressed.

| Quantity | Value |
|---|---|
| Wait of the first job, while the first worker pulled the image (not billed) | 376 s |
| Wait of the next job, on a worker that had the image | 0.9 s |
| `dotnet build src/Tertius.Demo -c Release` in a tree | 15 to 21 s |
| Three headless deck runs of 119 frames each, after the build | 62 s |

A build in a tree compiles every project, because the image's build outputs hold the paths under `/opt/tertius`. The image's build still restores the NuGet packages, so a tree's build finds them.
