#!/usr/bin/env bash
# Tool code (t1-8b4w), after stage-1b/tools/cost/cloud-jobs.sh: the cloud timing jobs of the acceptance, main against
# the branch on the RTX 2000 Ada. Run from the worktree's root, outside the dev shell:
#   bash cloud-cost.sh <out folder> <branch commit> [A] [B]
# Job A times the deck and the cabin, job B rect-lamps and the crowd. A GPU job may run 840 s, so two jobs of two scenes
# each fit with six runs per side. Each scene compares the branch with the main of its own job.
# Per scene, one command: run main once with the automatic exposure, take the median stops of its exposure.json, then
# run ab_run.sh with that fixed exposure for both trees, six runs each, in an order that reverses every round. Main's
# tree lacks the scripts of the cabin, rect-lamps and the crowd, so both trees read every script from the branch's tree.
# Send the jobs one after the other: two jobs that push the same commit at once fail one push (t2-a59b).
set -uo pipefail
out=$1; branch=$2
shift 2
jobs=${*:-A B}
mkdir -p "$out"
demo=src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo
median='import json, statistics, sys; print(statistics.median(e.get("stops", e.get("Stops")) for e in json.load(open(sys.argv[1]))))'
scene_cmd() { # <name> <script path as seen from a tree>
  local name=$1 script=$2
  printf '%s' "(cd main && dotnet build src/Tertius.Demo -c Release -nologo -v:q | tail -2 && $demo --script $script --headless --out ../exp-$name --size 1920x1080 --video none > ../exp-$name.log 2>&1 || { echo exposure run failed with \$?; tail -30 ../exp-$name.log; exit 1; }) && s=\$(python3 -I -c '$median' exp-$name/exposure.json) && echo \"$name exposure \$s\" && DEMO_ARGS=\"--size 1920x1080 --video none --exposure \$s\" bash main/tools/runpod/ab_run.sh $script 6 main branch && cp exp-$name/exposure.json out/exposure-main-auto.json && echo \$s > out/exposure-stops.txt && mv out out-$name"
}
trees=(--tree main=d8559eb583f5a7eceee0b57f74102bbb198eb7a6 --tree "branch=$branch")
outputs=(--output 'out-*/*/run-*/gpu-timings.json' --output 'out-*/*/run-*/exposure.json' --output 'out-*/clocks.csv' --output 'out-*/exposure-*')
[[ $jobs == *A* ]] && python3 -I tools/runpod/gpu_job.py submit --agent t1-8b4w-takes "${trees[@]}" \
  --run "$(scene_cmd deck ../branch/scripts/temporal-upscale-timing.json)" \
  --run "$(scene_cmd cabin ../branch/scripts/ter-kh4g-cabin-timing.json)" \
  "${outputs[@]}" --timeout 800 --out "$out/jobA" > "$out/jobA.log" 2>&1
[[ $jobs == *B* ]] && python3 -I tools/runpod/gpu_job.py submit --agent t1-8b4w-takes "${trees[@]}" \
  --run "$(scene_cmd rect ../branch/scripts/ter-kh4g-rect-lamps-timing.json)" \
  --run "$(scene_cmd crowd ../branch/scripts/ter-kh4g-crowd-timing.json)" \
  "${outputs[@]}" --timeout 800 --out "$out/jobB" > "$out/jobB.log" 2>&1
echo done
