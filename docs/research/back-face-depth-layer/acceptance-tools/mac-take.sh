#!/usr/bin/env bash
# Tool code (t1-8b4w), after stage-1b/tools/cost/mac-take2.sh: one Mac timing take of one scene, main against the branch,
# under one exclusive hold of the GPU lock. Run it from the worktree's root, inside the dev shell:
#   nix develop --command bash -c 'bash <this> <out> <scene> <first round> <rounds>'
# <scene> names scripts/<scene>.json of the branch's tree. Both sides read the same script.
#
# The fixed exposure: the first take of a scene runs main once with the automatic exposure, into
# <out>/<scene>/exposure-run, and writes the median stops of its exposure.json into <out>/<scene>/stops.txt. That run
# also warms the GPU and is not timed. A later take of the scene reads stops.txt and runs one untimed warm-up of main.
#
# The sides: main is the Release demo of d8559eb58, built in a second tree, and branch the Release demo of the branch.
# With two sides, a balanced order alternates: even rounds run main then branch, odd rounds branch then main. So each
# side runs as often first as second, and a drift over the take falls on both sides alike.
set -uo pipefail
out=$1; scene=$2; first=$3; rounds=$4  # a fifth argument --inner runs the body, under the lock
tree=$(pwd)
lock=$tree/tools/gpu-lock.sh
# gpu-clock, built from tools/gpu-calibration/gpu-clock.c (tools/README.md, "GPU calibration"). Stage 1b's copy in
# /private/tmp/tertius-survey-20261109/clockbin is gone, and the first takes of this record ran without a clock there.
export GPU_CALIBRATION_BIN=/private/tmp/t1-8b4w/clockbin
mkdir -p "$out/$scene"
log=$out/$scene/take-$first.log
bin_of() { echo "/private/tmp/t1-8b4w/bin-$1/Tertius.Demo"; }

take() {
  local stops_file=$out/$scene/stops.txt
  if [[ ! -s $stops_file ]]; then
    "$(bin_of main)" --script "scripts/$scene.json" --headless --out "$out/$scene/exposure-run" --video none \
      --in-flight 2 --pass-timestamps off > "$out/$scene/exposure-run.log" 2>&1 || { echo "exposure run failed"; return 1; }
    python3 -I -c 'import json, statistics, sys; print(statistics.median(e["stops"] for e in json.load(open(sys.argv[1]))))' \
      "$out/$scene/exposure-run/exposure.json" > "$stops_file"
  else
    "$(bin_of main)" --script "scripts/$scene.json" --headless --out "$out/$scene/warm-up-$first" --video none \
      --in-flight 2 --pass-timestamps off --exposure "$(cat "$stops_file")" > "$out/$scene/warm-up-$first.log" 2>&1
    rm -rf "$out/$scene/warm-up-$first"
  fi
  local stops; stops=$(cat "$stops_file")
  echo "$scene stops $stops"
  for ((r = first; r < first + rounds; r++)); do
    local order=(main branch)
    (( r % 2 == 1 )) && order=(branch main)
    for ((k = 0; k < 2; k++)); do
      local side=${order[$k]}
      local run=$out/$scene/$side/run-$r
      rm -rf "$run"; mkdir -p "$run"
      local start; start=$(date +%s)
      bash "$tree/tools/gpu-clock.sh" "$(bin_of "$side")" --script "scripts/$scene.json" --headless \
        --out "$run" --video none --in-flight 2 --pass-timestamps off --exposure "$stops" > "$run/run.log" 2>&1
      local code=$?
      python3 -I "$tree/tools/gpu_clock.py" record "$run/run.log" "$run/gpu-timings.json" > /dev/null 2>&1
      echo "$scene round $r position $k side $side exit $code seconds $(( $(date +%s) - start )) load $(uptime | sed 's/.*averages*: //')"
    done
  done
}
if [[ ${5-} == --inner ]]; then
  take
  exit $?
fi
bash "$lock" "$log" bash "$0" "$out" "$scene" "$first" "$rounds" --inner
echo "take $scene $first..$((first + rounds - 1)) exit $?" >> "$out/takes.log"
