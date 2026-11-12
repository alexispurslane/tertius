# The GPU workbench

The GPU workbench is a queue of cloud GPU jobs. An agent uses it to measure any change to the renderer: what the change saves in GPU time, and what it does to the image. Each job runs on an RTX 2000 Ada in a Runpod serverless worker and costs a few cents. Up to five jobs run at once. The workbench is the default place to measure. The Mac's GPU lock is only for the three cases in "What still needs the Mac".

The same queue runs CPU jobs on cheaper workers without a GPU: the official Stryker run of a hardening ("Tests in the cloud"). The $30 of credit must last until about 2026-12-07, and most of it goes to measurements of the engine. So the test suites run on the Mac by default, and the cloud runs them only on demand.

The one command is `tools/runpod/gpu_job.py submit`. [README.md](README.md) describes how the queue, the caps, the images and the endpoints work.

## The rules in short

1. Time a change that cuts GPU work as an A/B against main inside one job. Read the result as a relative gain, against the spread of the runs.
2. Compare images, a still or a film, inside one job as well.
3. Do not compare numbers from two jobs, or from a job and the Mac. Workers differ by about 0.4 % between jobs, and the card is not the Mac.
4. A gain on the card shows on the Mac at about 2.4 times its size in ms. The frame-time goal, 12 ms on the Mac, holds only after one timed Mac run of the final candidate confirms it, before the merge.
5. Run the script, never the key. The script reads the key itself. Never read, print or copy the key's file.

## Time a change: an A/B against main

1. Commit the change on a branch. The commit need not be pushed. The script publishes each tree's commit under a hidden ref on GitHub, and the worker fetches it. To test a patch file, apply it on a branch and commit it there.
2. From the repository, run one job with two trees, `main` and your variant, five runs per side:

   ```bash
   python3 -I tools/runpod/gpu_job.py submit --agent <your name> \
       --tree main=main --tree cut=HEAD \
       --run 'bash main/tools/runpod/ab_run.sh scripts/temporal-upscale-timing.json 5 main cut' \
       --output 'out/*/run-*/gpu-timings.json' --output 'out/clocks.csv' \
       --timeout 600 --out /tmp/<your name>/ab
   ```

3. Read the result:

   ```bash
   python3 -I tools/runpod/ab_report.py /tmp/<your name>/ab --base main
   ```

`ab_run.sh` builds the Release demo of each tree, then runs each tree's demo headless at 1920 × 1080 with `--video none`. One untimed run of the first tree warms the card first, because the first run on a fresh worker was about 0.5 ms slower in three jobs. It reverses the order of the trees in each round: main and cut, then cut and main. Thus a drift of the card's clock or heat falls on both sides alike. It logs the card's clocks to `out/clocks.csv`. The script path is inside each tree, so a variant can also change its script. To pass renderer options, set `DEMO_ARGS` before the command, and include the default options: `DEMO_ARGS='--size 1920x1080 --video none --upscaling off' bash main/tools/runpod/ab_run.sh ...`. Name more trees to compare several variants with main in one job.

The report prints, for each tree, the GPU frame span of each run without the `capture` pass, and their median. The `capture` pass is the headless readback over PCIe, which a window does not do (ticket ter-fw73). Then it prints, for each variant:

- The change of the median against main, in ms and in percent.
- A verdict. When every run of one side lies below every run of the other side, it prints "every run differs". Otherwise it prints "the runs overlap: within the noise".
- The estimate on the Mac: the change in ms times 2.4.
- The passes whose medians moved most.

### How many runs, and how to read them

Two A/A jobs on 2026-11-12 ran the same commit as both trees, five runs per side, at the same time on two workers. They show the noise:

| Quantity | Job 1 | Job 2 |
|---|---:|---:|
| Median frame span of main, without the capture | 8.895 ms | 8.863 ms |
| Median of the copy | 8.890 ms | 8.832 ms |
| Change of the copy against main | -0.1 % | -0.3 % |
| Spread of one side's five runs | 0.07 to 0.14 ms | 0.08 ms, and 0.59 ms with one slow first run of 9.37 ms |
| Largest change of a pass median | 0.002 ms | 0.002 ms |
| SM clock, mean (lowest to highest) | 2379 MHz (2130 to 2475) | 2390 MHz (2130 to 2490) |

Those jobs had no warm-up run yet. A third A/A job with the warm-up, three runs per side, gave spreads of 0.009 and 0.011 ms and a change of -0.004 ms. Five runs per side is the default. A median of five also ignores one slow run. Read a result in this order:

1. If the report says "every run differs", the change of the frame span is real. With the warm-up, the runs of one side lay within about 0.01 ms, so a change of about 0.02 ms or more can separate them. Without it, a change took about 0.15 ms. One A/A job measured the smaller spread, so trust it after more jobs confirm it.
2. If the runs overlap, look at the passes that you changed. A pass median repeats within 0.002 ms between two runs of the same commit. So a pass that moved by 0.01 ms or more moved because of the change.
3. If a smaller gain matters, run 9 runs per side in one job. Do not add a second job, because its worker differs.

A five-run A/B of two trees takes about 150 s on the worker and counts about $0.04.

## Compare images: a quality A/B

The same job can make stills and films of both trees. Two runs of the same commit on one worker give the same image to the bit: the A/A still above had a mean difference of 0.0. So every difference between the trees' images comes from the change. An image from another job or from the Mac can differ, because determinism holds per computer only.

- A still comes from a `capture` step of a script, as `<name>.png`. Make stills with one run and without `--video none`:

  ```bash
  python3 -I tools/runpod/gpu_job.py submit --agent <your name> \
      --tree main=main --tree cut=HEAD \
      --run "DEMO_ARGS='--size 1920x1080' bash main/tools/runpod/ab_run.sh scripts/cabin-light.json 1 main cut" \
      --output 'out/*/run-1/cabin.png' --timeout 400 --out /tmp/<your name>/stills
  ```

- The images come back under the `--out` folder at the same paths, here `/tmp/<your name>/stills/out/main/run-1/cabin.png` and `.../out/cut/run-1/cabin.png`.
- A job sends back at most 8 MB in base64, about 6 MB of PNG. A 1920 × 1080 still took 0.8 to 2.3 MB, so two or three stills fit in one job. `result.json` lists the files past the limit under `skipped`, with their sizes.
- For more images, and for films, compute the metric inside the job and send back numbers and small crops. The dev shell has Python with numpy, scipy and pillow, and ffmpeg. Run the metric's script from the tree, for example the daub score of ticket t7-3e22, in a `--run` command after `ab_run.sh`.
- Several still jobs at once fetch more images: give each job one image or one view.

## What still needs the Mac

The Mac's GPU lock (`tools/README.md`, "The GPU lock") is only for these:

1. Changes whose cost Linux does not pay: MoltenVK's encoder breaks, the count of dispatches and barriers, and other behavior of MoltenVK or Metal. The card on Linux runs those at almost no cost, so it cannot show their gain.
2. Changes that depend on Apple's shader compiler: unrolling, register pressure and occupancy. NVIDIA's compiler makes different choices.
3. One confirmation take of the final candidate before its merge, for the 12 ms goal.

The factor of 2.4 holds only for a change of the amount of work. A change of the form of the work can move the two GPUs in opposite directions. On 2026-11-12 (t2-gfq5), sixteen bilinear corner reads in place of nine gathers saved about 0.5 ms of the deck's frame span on the Mac. On the RTX 2000 Ada, the same change cost 0.80 ms (+8.9 %). So a change of texture access kind (gather against sample), register use or code size needs a run on both machines before its merge.

For everything else, measure on the workbench, and give the Mac's lock to the agents that need it.

## Several jobs at once

The GPU endpoint runs up to five workers, so an agent can measure several variants at once. Each call of the script is one job and waits for it. Start each call in the background, and wait for all of them:

```bash
for variant in cut-a cut-b cut-c; do
    python3 -I tools/runpod/gpu_job.py submit --agent <your name> \
        --tree main=main --tree "$variant=$variant" \
        --run "bash main/tools/runpod/ab_run.sh scripts/temporal-upscale-timing.json 5 main $variant" \
        --output 'out/*/run-*/gpu-timings.json' --timeout 600 --out "/tmp/<your name>/$variant" \
        > "/tmp/<your name>/$variant.log" 2>&1 &
done
wait
```

An agent's own tool can also start each call as one background command. Compare each variant with the `main` of its own job, not with another job's main. If more than five jobs wait, the rest queue for up to two hours. The RTX 2000 Ada is scarce at times: on 2026-11-12 a job waited 19 minutes for a card. Two A/A jobs at once each finished in about 150 s on 2026-11-12.

## Tests in the cloud

Where each kind of test run goes:

| Run | Where by default | Command | Measured cost and wall time |
|---|---|---|---|
| The official Stryker run of a hardening | Cloud CPU workers, one module per worker, all at once | `tools/stryker/cloud.py` | Core 2,731 s for $0.056, Simulation 1,866 s for $0.039, Physics below |
| The listed mutants of `tools/mutate.py` | The Mac | `tools/mutate.py` | On a GPU worker, about 63 s per Rendering mutant against 40 s on the Mac, plus 175 s of build |
| The pre-commit hook, and the `Checked` and `Release` suites before a merge | The Mac | `tools/test-all.sh` | Free on the Mac |
| The CPU-only test projects, to keep them off the Mac | Cloud CPU workers, when an agent wants it | `gpu_job.py submit --kind cpu` with `dotnet test --project <project> -c Checked` | Not measured on a CPU worker. On one GPU worker, all of them took about 4 min and $0.05. |
| One pass of the GPU tests before a merge | The Mac | `tools/test-all.sh` | Free on the Mac |
| The Rendering tests in GPU shards | Cloud GPU workers, only on demand, when an agent is blocked on the Mac | One `gpu_job.py submit` per shard | 6 shards: about 7.3 min and $0.43 |

Run the official Stryker run of a hardening in the cloud, from the branch's checkout on the Mac, without the dev shell:

```bash
python3 -I tools/stryker/cloud.py main /tmp/<ticket>-stryker --agent <your name>
```

The script pushes the branch's head commit to a hidden ref, as every job does. It sends one CPU job per module with changed lines, and each job runs `tools/stryker/changed.py` on its module with the official settings. The script waits for all jobs. Then it prints the same table, evidence and validity as `changed.py`, and writes the merged `results.json`. Each module's report and logs land in `/tmp/<ticket>-stryker/<module>/`. The modules run at once, so the wall time is that of the slowest module plus the wait for a worker. On the Mac, the same modules run one after another under the machine's Stryker lock.

A cloud run needs no Mac lock and builds nothing in your worktree. So you can build, test and commit in the worktree while it runs. The rules for a valid run are those of `tools/README.md`, "The official Stryker run". A run that prints NOT VALID proves nothing there either.

Only the official Stryker run goes to the cloud by default. Run the other tests on the Mac. They are free there, and the $30 must last. If the Mac blocks you for a long time, you can send GPU shards of the Rendering tests. They do not replace the Mac pass of the GPU tests before a merge. A shard builds the test project, then runs the test assembly directly with a list of classes:

```bash
python3 -I tools/runpod/gpu_job.py submit --agent <your name> --tree main=HEAD \
    --run "cd main && dotnet build tests/Tertius.Rendering.Tests -c Checked -nologo -v:q" \
    --run "mkdir -p out; cd main && dotnet tests/Tertius.Rendering.Tests/bin/Checked/net11.0/Tertius.Rendering.Tests.dll -maxThreads 2 -longRunning 60 -noColor -result-ctrf ../out/shard.json -class Tertius.Rendering.Tests.<Class> ..." \
    --output out/shard.json --timeout 800 --out /tmp/<your name>/shard0
```

Use `-maxThreads 2`. At the default parallelism, the GPU tests run out of the card's 16 GB (ticket ter-net6). `dotnet test` does not work on a worker: its test host fails the handshake. Split the classes into shards of similar time, at most five at once. On 2026-11-12, six shards ran 2,877 tests, and 9 failed on Linux (see "Linux failures" in [README.md](README.md)).

## Costs and caps

- A GPU worker costs $0.00016 per second, $0.58 per hour, while it runs: its start, the job and 10 s of idle time. A CPU worker costs $0.00002 per second, $0.07 per hour. A worker that waits, or pulls the image, costs nothing.
- The script counts each job as its execution time plus 70 s, times the price. Examples: a five-run A/B $0.04, two stills $0.03, a short job of a few seconds $0.012.
- The caps are $3 per UTC day from 2026-11-13 and $30 in total, for all agents and both kinds together. The $30 must last until about 2026-12-07. Before it sends a job, the script adds the job's worst case to the day's and the total spending and to the jobs in flight. If the sum passes a cap, it refuses the job and sends nothing.
- Keep `--timeout` close to the real need. The worst case, and the cost of a cancelled job, follow it. A cancel does not stop a command that already runs.
- The audit log is `~/.local/state/tertius/gpu-jobs/jobs.jsonl`, one line per job.
- The script reads the key's file from the path in `~/.config/tertius/runpod-key-file`. An agent never reads, prints or copies the key, and never puts a credential in a job. The repository and the image are public, so a commit holds no secret.

## Pitfalls

- A command's text is in its shell's command line. So `pkill -f` or `pgrep -f` with a pattern from the command finds the command itself. Match a process by its name, with `pgrep -x`.
- When a command ends, every process that it started in the background stops too. Start and use such a process in the same command.
- If a command fails, Runpod marks the job FAILED. `result.json` keeps the handler's own result in `status` and Runpod's in `runpodStatus`.
- A tree has no `docs/` and no `.tickets/`, because the worker does not fetch them. No build or run reads them. A job that needs a file from `docs/` must copy it elsewhere in its commit.
- Each tree's commit becomes public. Before the push, the script scans the new commits for secrets, and a finding refuses the job. Never commit a secret, also not on a branch.
- If a job's commits change `flake.nix`, `flake.lock` or `native/`, the job needs an image of that dev shell. Without one, the script refuses the job and names the missing image. Build it with a tag, then send the job again: `git push --no-verify git@github.com:alexispurslane/tertius.git <commit>:refs/tags/gpu-image-<name>`. The build takes about 14 minutes. The script then creates a new endpoint for that image by itself ([README.md](README.md), "A new dev shell").
- A refusal that names "0 workers" means that the watchdog or the orchestrator stopped the endpoints. Wait for the orchestrator.
