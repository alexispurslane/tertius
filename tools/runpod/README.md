# Cloud jobs on Runpod: how the queue works

An agent runs a cloud job on a Runpod serverless endpoint. A GPU job is, for example, a build and a timing run of the demo. A CPU job is, for example, the official Stryker run of one module. The agent calls one script, `tools/runpod/gpu_job.py`, and the script keeps the spending within the caps. [WORKBENCH.md](WORKBENCH.md) is the guide for agents: the A/B against main, the quality A/B, what still needs the Mac, the cloud tests, and the costs. This file describes how the queue works. Tickets ter-94uo, ter-a5ev, ter-k7fd and t8-yd4j made the parts.

| Part | What it does |
|---|---|
| `gpu_job.py` | It runs on the Mac. It scans and publishes the trees' commits and picks the endpoint. It checks the caps, sends the job and writes the audit log. |
| `handler.py` | It runs on the worker. It fetches each tree's commit, runs the commands in the dev shell and collects the output files. |
| `worker.py` | The image's entry. The Runpod SDK gives it each job, and it calls the handler in a fresh work folder. |
| `Dockerfile`, `provision.sh` | The image: the dev shell, the source of one commit with its Release demo built, and the worker. |
| `.github/workflows/gpu-image.yml` | It builds the image on GitHub and pushes it to `ghcr.io/alexispurslane/tertius-gpu:<commit>`. |
| `endpoint.json` | The configuration of the script: the image repository, the public repository, the caps, and each kind's price, workers and time limits. |
| `tools/stryker/cloud.py` | The official Stryker run with one CPU job per module ("Cloud tests" below). |
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
- `--kind gpu`, the default, runs the job on an RTX 2000 Ada. `--kind cpu` runs it on a CPU worker of 2 vCPUs and 4 GB of memory, with no GPU, at an eighth of the price.
- `--timeout` gives the seconds of all commands together, a positive number. It is at most the kind's `job_seconds` in `endpoint.json`: 840 s for a GPU job and 7,140 s for a CPU job. The handler stops the commands at that limit and still sends back the log and the files.
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

An image of another branch never runs the job. "Main" is `refs/remotes/origin/main`, main as the repository last fetched it from GitHub. The one image serves both kinds: on a CPU worker, the dev shell starts without a GPU.

Each kind has its own endpoints, one per image. Runpod's list of endpoints is the one store of the live endpoints: the script finds an endpoint by its kind and its image, and no file holds an id. The script's own endpoints are named `tertius-<kind>-<12 characters of the commit>-<4 random hex digits>`. The first endpoints have no random part, or the bare name `tertius-gpu`. The script deletes no other endpoint. Runpod keeps the name of a deleted or failed endpoint for up to 90 minutes, and it refuses a new endpoint of that name. So each creation draws a new random part.

| Kind | Machine | Workers | Price | Time limit per job | Disk |
|---|---|---:|---:|---:|---:|
| `gpu` | RTX 2000 Ada, from the 16 GB pool `AMPERE_16` without its other three cards | 5 | $0.00016/s, $0.58/h | 840 s of commands, 900 s in all | 60 GB |
| `cpu` | `cpu3c`, compute-optimized, 2 vCPUs, 4 GB of memory | 3 | $0.00002/s, $0.07/h | 7,140 s of commands, 7,200 s in all | 20 GB, the most for 2 vCPUs |

Runpod limits the account to 10 workers, summed over the maximums of all its endpoints. The kinds hold 8 of them. The other 2 are the headroom of an image switch. The new endpoint of either kind starts with the 2 free workers, beside the busy old one. When the old one goes, the new one grows to its kind's workers. The script never lowers an endpoint to make room. Under the lock, the script routes each job in three steps. Apart from the quota, each step looks only at the endpoints of the job's kind. So a GPU image switch never deletes the CPU endpoint and takes its workers. An idle old endpoint of a kind waits for the next job of its own kind:

1. It deletes each endpoint of the kind that meets three conditions: it does not run the chosen image, no job in flight names it, and no worker runs on it. An old endpoint thus goes after its last job, and no job of it is cut. Its workers return to the quota.
2. If no endpoint of the kind runs the chosen image, the script creates one. It gets the workers that the quota leaves, at most the kind's workers. If the quota leaves none, the job runs on the kind's endpoint of the best fitting image. If the kind has no such endpoint, the script refuses the job.
3. If the quota leaves room, the script raises the live endpoint to its kind's workers.

`gpu_job.py endpoint-request [--kind cpu] <commit>` prints the request that creates an endpoint, and sends nothing.

When the spending nears its limit, the orchestrator's watchdog stops all endpoints. It first writes the file `~/.local/state/tertius/gpu-jobs/stopped-by-watchdog`, and then it sets every endpoint to a maximum of 0 workers. While that file exists, the script refuses every job and deletes nothing. The orchestrator removes the file when it restarts the endpoints. To stop the endpoints by hand, write the same file first. The watchdog does not take the script's lock. So the script reads the list of endpoints and the file again before each deletion, after a creation and before it raises an endpoint. If one of those reads shows a stop, the script deletes its new endpoint again and refuses the job.

An endpoint at 0 workers without the file is on its way out. A delete in Runpod's console holds an endpoint at 0 workers for a few seconds before it is gone. On 2026-11-13, the script took such a delete for the watchdog's stop and refused a CPU job (ticket t8-j5oc). The script now never routes a job to such an endpoint, never raises it and never deletes it.

This narrows the race with the watchdog but does not close it. A watchdog that listed the endpoints before the script created one does not stop the new one in that round. A stop that lands between the script's last read and its raise of an endpoint is undone until the watchdog's next round. That round comes 5 minutes later, and the caps stay a separate guard. If the orchestrator restarts the endpoints after a stop, the sum of their maximums must stay within 10: 5 for the GPU endpoint and 3 for the CPU endpoint.

So a new image replaces the old endpoint without a drain: new jobs go to the new endpoint, and the jobs of the old one finish there. The caps and the audit log count the jobs of every endpoint, because each job records its own endpoint.

## A new dev shell

If a job's commits change the dev shell and no image has it, the script refuses the job and names the missing image, `ghcr.io/alexispurslane/tertius-gpu:<commit>`. Build it first. The workflow `gpu-image` on GitHub builds the image from the files of one commit, with `git archive`, in about 14 minutes. Three events start it:

- A push to main that changes `flake.nix`, `flake.lock`, `native/`, the workflow, or the image's own files: `Dockerfile`, `provision.sh`, `handler.py` and `worker.py`.
- A push of a tag `gpu-image-<anything>` to GitHub, for any commit: `git push --no-verify git@github.com:alexispurslane/tertius.git <commit>:refs/tags/gpu-image-<name>`.
- A manual run: `gh workflow run gpu-image.yml --repo alexispurslane/tertius`.

When the build passed, the next job of that dev shell creates the new endpoint itself.

## The caps

The caps are $30 in total and a cap per UTC day, for all agents and both kinds together. `daily_caps_usd` in `endpoint.json` maps the first day of each daily cap to its amount: $10 through 2026-11-12, and $3 from 2026-11-13. The $30 must last until about 2026-12-07. Before the script sends a job, it adds the job's worst case to two amounts:

1. The spending, from Runpod's bill and the audit log. The bill (`GET /v2/billing`) holds the account's other spending too, such as pods. But Runpod bills by the hour, and an hour's bill can still grow after the hour ends. On 2026-11-12, the bill of 17:00 UTC grew from $0.081 at 18:12 to $0.219 at 19:02, and then it stayed. So the script counts an hour's bill as final from the start of the second hour after its end (`BILL_FINAL_HOURS` = 2), one hour of margin. The script counts the bill of each final hour as it is. Each hour after the last final hour counts the larger of its own bill and the logged costs of the jobs that ended in it. So the log's flat 70 s per job stops counting once the hour's bill is final, and the count is never less than the bill. A pod's bill in one hour does not hide the unbilled jobs of another hour. Three cases err high, which is safe for a cap:

- A job that ran across an hour's end counts whole in the hour of its end, beside the part of it that an earlier hour billed.
- A job that a later run settled (see "Jobs in flight and the audit log") ends when that run saw it, so it counts again in the open hours.
- A cancelled job ends at its expiry, because its handler bills until the job's own limit.

One case errs low: an open hour that holds both spending that the log lacks, such as a pod, and jobs whose bill is not in yet counts only the larger of the two. The day's total, the month's total and the hourly records come from the same records, and they agreed to the cent on 2026-11-12. The daily cap uses today's sums, and the total cap uses the sums of the last 12 months, the account's whole life.
2. The worst cases of the jobs in flight, on every endpoint.

If either sum passes its cap, the script refuses the job, sends nothing and logs the refusal. A file lock spans the check and the record of the new job in flight. So two agents cannot both take the last of a cap.

One cost model serves the worst case and the logged cost: the seconds, plus 60 s for a worker's start, plus 10 s of idle time after the job, times the price per second of the job's kind. For the worst case, the seconds are the execution timeout: the commands' time plus 60 s for the handler, and at most the endpoint's timeout. For the logged cost, they are Runpod's execution time. A job on a warm worker bills no start, so the log overcounts rather than undercounts. On 2026-11-12, the bill of the final hours from 12:00 to 15:00 UTC was 0.81 times the log's estimate of the jobs that ended in them. The bill also holds a disk charge per worker, which the model leaves out.

Runpod bills a worker only while it runs: its cold start, 10 s here with FlashBoot, its jobs and its 10 s of idle time. It does not bill the image pull, or a worker that is initializing, idle or throttled ([pricing](https://docs.runpod.io/serverless/pricing)).

## Jobs in flight and the audit log

The state folder `~/.local/state/tertius/gpu-jobs/` holds two stores:

- `pending/` holds one file per job in flight, with its endpoint, its image, its worst case and, after Runpod accepts the job, its id.
- `jobs.jsonl` holds one JSON line per settled job, a refusal too. The line holds the time, the agent, the endpoint, the image and the commands. It also holds the trees with their refs, commits and hidden refs, the job's id, the seconds of execution (`gpuSeconds`, for both kinds), the cost, the result, the exit codes and the time at which the script saw the job end (`ended`). A line from before ticket ter-xfcq has no `ended`. For such a line, the caps take its send time plus its wait and its execution.

`python3 -I tools/runpod/gpu_job.py stats` reads the audit log and prints the cost per job that ran, for all jobs, each kind and each agent. It gives the count, the mean, the standard deviation, the half width of the 95 % interval of the mean (Student's t), the median, the minimum and the maximum. It prints two figures: the settled estimate, and the execution time alone times the kind's price. Runpod bills by the hour, not by the job. So the third figure is one line: the serverless bill of the final hours since the first job, beside the settled estimate of the jobs that ended in them. The command sends no job and changes nothing.

When a job ends, the script moves it from `pending/` to the log in one step under the lock. If the script stops first, for example from a lost network or a kill, the job stays in `pending/` and counts at its worst case. The next run of the script asks Runpod for that job's final status and settles it. While a run waits for its job, it holds the job's owner lock, `pending/<name>.owner`. Another run does not settle such a job, so the job's endpoint stays and its own run gets its result. The system releases the lock when the run dies. A job that Runpod no longer knows costs its worst case. A job counts until its queue time and its timeout pass. If Runpod reports no end by then, the log records it as `lost` at its worst case. A failed push of the commits makes no job, and neither does an HTTP error of `/run`. The log records such a job as `rejected` at no cost. A failed git command, registry call or Runpod call before the job is recorded refuses the job, and the log says why.

## Rules for a job's commands

- A command runs as `bash -c '<command>'` inside the dev shell. Its text is in that shell's command line. So `pkill -f <pattern>` with a pattern from the command stops the command itself.
- When a command ends, every process that it started in the background stops too, such as a clock log. Start and use such a process in the same command. This also stops dotnet's build servers, so join two builds into one command with `&&` to keep the compiler warm.
- The commands see none of the worker's `RUNPOD_*` variables.
- If a command fails, the Runpod SDK marks the whole job FAILED. The audit log and `result.json` keep the handler's own result (`failed`, `timed out` or `refused`), and `result.json` keeps Runpod's status as `runpodStatus`.
- The worker's image has no `bc`. Use `date +%s%N` and shell arithmetic for a time.

## Cloud tests

`tools/stryker/cloud.py` runs the official Stryker run of `tools/stryker/changed.py` with one CPU job per module. Each job's tree is a shallow clone of the head commit. Its command fetches the tree's history without file contents (`git fetch --filter=blob:none --unshallow`), so that `changed.py` finds the merge base and reads the changed files on demand. Then it restores the Stryker tool and runs `changed.py <base> ../out --modules <module>`. The job sends back the module's `results.json`, its Stryker configuration, Stryker's console output and the process samples. Stryker's log (8 to 21 MB) and its mutation report (6 to 85 MB) do not fit the 8 MB that a result carries, so they stay on the worker. `results.json` holds the rows, the evidence and the validity. `cloud.py` refuses an output folder that holds files, so that no earlier result counts for a module whose job gave none. `cloud.py` merges the modules' results and prints them with `changed.py`'s own table and validity rules. WORKBENCH.md, "Tests in the cloud", gives the command.

The CPU kind uses the cheapest machine that ran Stryker within its limits. Measured on 2026-11-12 on `cpu3c` with 2 vCPUs and 4 GB:

| Measurement | Value |
|---|---|
| The peak of the job's anonymous memory, during Stryker's builds and its initial test run | 2,295 MB of 4,096 MB, with no out-of-memory kill and no swap |
| A `Checked` build of the solution | 157 s |
| Stryker's initial test run of Core's 4,625 tests | 41 to 94 s, against about 27 s on the Mac |
| Core, 0 mutants in its 3 spans | 2,731 s, $0.056, valid |
| Simulation, 6 mutants in its 4 spans, all killed | 1,866 s, $0.039, valid |
| Physics, 6 mutants in its 4 spans, all killed, from the base of ter-773q | 641 s, $0.0145, valid |

The 1-minute load in a job reads the shared host: a mean of 16 to 112, at most 204. It does not measure the worker's own contention. `os.cpu_count()` also reads the host and gives 192, against 2 processors that the job may use. So the load rule of `changed.py` allows a load of 3,360 on a worker, and in effect never makes a run not valid there. The timeout share rule still applies. The three runs ended with no timeout.

On the Mac, the same modules run one after another, under one Stryker lock: Core 2,674 s and Simulation 3,813 s on tiunr-e8xk. The cloud runs them at once on separate workers, so a full hardening takes the time of its slowest module. For the three runs above, that was 2,731 s and $0.11 in total, against more than 6,487 s for Core and Simulation alone on the Mac. A larger worker does not pay: the per-test coverage capture takes most of a run, and it uses about one core and a half.

### Linux failures

These tests fail on the Linux workers and pass on the Mac. A cloud run of their project is not clean until they are fixed:

- Host: 215 of 2,055 tests run out of the card's memory at the default parallelism (ter-net6).
- Host: the edit command's closing card draws text into its left margin (ter-j4h9).
- Rendering: an FSR 3.1 host constant differs by 1 ulp on x86-64 (ter-qjyo).
- Rendering: 8 more tests fail on the RTX 2000 Ada: sphere, tube and accumulator light tests, the prefilter of a uniform map (16 and 32), the light-age diagnostic, the young history of the diffuse filter, and FSR 3.1's reference image.
- Slop: `CommitReportTests.TheCommitReportShowsTouchedFindingsBeforeTheProjectAddendum`.

The Core, Simulation and Physics tests pass on the workers since ter-zkdz. That ticket made four tests compare two runs on one machine, and the recorded results only on arm64.

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
