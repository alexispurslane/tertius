# The GPU workbench

The GPU workbench is a queue of cloud GPU jobs. An agent uses it to measure any change to the renderer: what the change saves in GPU time, and what it does to the image. Each job runs on an RTX 2000 Ada in a Runpod serverless worker and costs a few cents. Up to six jobs run at once. The workbench is the default place to measure. The Mac's GPU lock is only for the three cases in "What still needs the Mac".

The one command is `tools/runpod/gpu_job.py submit`. `tools/README.md`, "GPU jobs on Runpod", describes how the queue, the caps and the image work.

## The rules in short

1. Time a change that cuts GPU work as an A/B against main inside one job. Read the result as a relative gain, against the spread of the runs.
2. Compare images, a still or a film, inside one job as well.
3. Do not compare numbers from two jobs, or from a job and the Mac. Workers differ by about 0.4 % between jobs, and the card is not the Mac.
4. A gain on the card shows on the Mac at about 2.4 times its size in ms. The frame-time goal, 12 ms on the Mac, holds only after one timed Mac run of the final candidate confirms it, before the merge.
5. Run the script, never the key. The script reads the key itself. Never read, print or copy the key's file.

## Time a change: an A/B against main

1. Commit the change on a branch. The commit need not be pushed, because the script sends the diff from the image's commit. To test a patch file, apply it on a branch and commit it there.
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

Those jobs had no warm-up run yet. Five runs per side is the default. A median of five also ignores one slow run. Read a result in this order:

1. If the report says "every run differs", the change of the frame span is real. With five runs per side, that takes about 0.15 ms or more on the card, about 0.35 ms on the Mac.
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

For everything else, measure on the workbench, and give the Mac's lock to the agents that need it.

## Several jobs at once

The endpoint runs up to six workers, so an agent can measure several variants at once. Each call of the script is one job and waits for it. Start each call in the background, and wait for all of them:

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

An agent's own tool can also start each call as one background command. Compare each variant with the `main` of its own job, not with another job's main. If more than six jobs wait, the rest queue for up to two hours. Two A/A jobs at once each finished in about 150 s on 2026-11-12.

## Costs and caps

- A worker costs $0.00016 per second, $0.58 per hour, while it runs: its start, the job and 10 s of idle time. A worker that waits, or pulls the image, costs nothing.
- The script counts each job as its execution time plus 70 s, times the price. Examples: a five-run A/B $0.04, two stills $0.03, a short job of a few seconds $0.012.
- The caps are $10 per UTC day and $30 in total, for all agents together. Before it sends a job, the script adds the job's worst case to the day's and the total spending and to the jobs in flight. If the sum passes a cap, it refuses the job and sends nothing.
- Keep `--timeout` close to the real need. The worst case, and the cost of a cancelled job, follow it. A cancel does not stop a command that already runs.
- The audit log is `~/.local/state/tertius/gpu-jobs/jobs.jsonl`, one line per job.
- The script reads the key's file from the path in `~/.config/tertius/runpod-key-file`. An agent never reads, prints or copies the key, and never puts a credential in a job. The repository and the image are public, so a commit holds no secret.

## Pitfalls

- A command's text is in its shell's command line. So `pkill -f` or `pgrep -f` with a pattern from the command finds the command itself. Match a process by its name, with `pgrep -x`.
- When a command ends, every process that it started in the background stops too. Start and use such a process in the same command.
- If a command fails, Runpod marks the job FAILED. `result.json` keeps the handler's own result in `status` and Runpod's in `runpodStatus`.
- A tree holds the image's copy of `docs/` and `.tickets/`, because a patch leaves them out. No build or run reads them, and research files there grow by megabytes an hour. A job that needs a newer file from `docs/` must copy it elsewhere in its commit.
- A job's input is at most 10 MB. If the diff from the image's commit grows past that, a newer image must replace it. The workflow `gpu-image` builds it, and the orchestrator runs `gpu_job.py use-image <commit>` while no job is in flight.
