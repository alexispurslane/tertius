#!/usr/bin/env bash
# Throwaway (t1-stwp): the cloud timing jobs of the cost of stage 1b. Run from the worktree's root, outside the dev
# shell: bash cloud-jobs.sh <out folder> [A] [B] [C]. Without a job name it sends A and then C.
# Job A times the deck and the cabin, job B rect-lamps and the crowd. Main's tree lacks the scripts of the cabin and
# rect-lamps, so A failed at the cabin and B failed at once, both with exit code 2. Job C times the cabin, rect-lamps
# and the crowd, with each script read from the tree script, the branch's commit adf373ae0. So the record takes the
# deck from A and the other scenes from C. A and B ran with an earlier version of this file, without the clause that
# prints the exposure run's log on a failure.
# Send the jobs one after the other: two jobs that push the same scratch commit at once fail one push (t2-a59b).
# Per scene, one command: build main, run main once with the automatic exposure, take the median stops of its
# exposure.json, then run ab_run.sh with that fixed exposure for every side: main layer ten off, five runs each.
set -uo pipefail
out=$1
shift
jobs=${*:-A C}
mkdir -p "$out"
demo=src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
median='import json, statistics, sys; print(statistics.median(e.get("stops", e.get("Stops")) for e in json.load(open(sys.argv[1]))))'
scene_cmd() { # <name> <script path as seen from a tree>
  local name=$1 script=$2
  printf '%s' "(cd main && dotnet build src/Tertius.Demo -c Release -nologo -v:q | tail -2 && $demo --script $script --headless --out ../exp-$name --size 1920x1080 --video none > ../exp-$name.log 2>&1 || { echo exposure run failed with \$?; tail -30 ../exp-$name.log; exit 1; }) && s=\$(python3 -I -c '$median' exp-$name/exposure.json) && echo \"$name exposure \$s\" && DEMO_ARGS=\"--size 1920x1080 --video none --exposure \$s\" bash main/tools/runpod/ab_run.sh $script 5 main layer ten off && cp exp-$name/exposure.json out/exposure-main-auto.json && echo \$s > out/exposure-stops.txt && mv out out-$name"
}
trees=(--tree main=ab54c785e --tree layer=refs/scratch/ter-kh4g-1b-layer --tree ten=refs/scratch/ter-kh4g-1b-ten --tree off=refs/scratch/ter-kh4g-1b-off)
outputs=(--output 'out-*/*/run-*/gpu-timings.json' --output 'out-*/*/run-*/exposure.json' --output 'out-*/clocks.csv' --output 'out-*/exposure-*')
[[ $jobs == *A* ]] && python3 -I tools/runpod/gpu_job.py submit --agent t1-stwp-cost "${trees[@]}" \
  --run "$(scene_cmd deck scripts/temporal-upscale-timing.json)" \
  --run "$(scene_cmd cabin scripts/ter-kh4g-cabin-timing.json)" \
  "${outputs[@]}" --timeout 1000 --out "$out/jobA" > "$out/jobA.log" 2>&1
[[ $jobs == *B* ]] && python3 -I tools/runpod/gpu_job.py submit --agent t1-stwp-cost "${trees[@]}" --tree script=adf373ae0 \
  --run "$(scene_cmd rect scripts/ter-kh4g-rect-lamps-timing.json)" \
  --run "$(scene_cmd crowd ../script/scripts/ter-kh4g-crowd-timing.json)" \
  "${outputs[@]}" --timeout 1000 --out "$out/jobB" > "$out/jobB.log" 2>&1
[[ $jobs == *C* ]] && python3 -I tools/runpod/gpu_job.py submit --agent t1-stwp-cost "${trees[@]}" --tree script=adf373ae0 \
  --run "$(scene_cmd cabin ../script/scripts/ter-kh4g-cabin-timing.json)" \
  --run "$(scene_cmd rect ../script/scripts/ter-kh4g-rect-lamps-timing.json)" \
  --run "$(scene_cmd crowd ../script/scripts/ter-kh4g-crowd-timing.json)" \
  "${outputs[@]}" --timeout 1200 --out "$out/jobC" > "$out/jobC.log" 2>&1
echo done
