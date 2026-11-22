#!/usr/bin/env bash
# Throwaway (t1-stwp): one Mac timing take of the cost of stage 1b, for one scene, under one exclusive hold of the GPU
# lock. Run it from the worktree's root, inside the dev shell (gpu-lock.sh fails under the Mac's own bash, t1-fudm):
#   nix develop --command bash -c 'bash /private/tmp/ter-kh4g/1b/cost/tools/mac-take2.sh <out> <scene> <first round> <rounds>'
# <scene> names scripts/<scene>.json. The take holds the lock once and runs every run of it back to back.
#
# The fixed exposure: the first take of a scene runs main's binary once with the automatic exposure, into
# <out>/<scene>/exposure-run, and writes the median stops of its exposure.json into <out>/<scene>/stops.txt. That run
# also warms the GPU and is not timed. Every later run of the scene, on every side, passes --exposure <those stops>.
# A later take of the same scene reads stops.txt and runs one untimed warm-up of main instead.
#
# The sides: main is the frozen binary of ab54c785e. layer, ten and off are modes of the frozen probe binary, chosen by
# TERTIUS_BACK_FACE_PROBE. One round runs every side once. Round r takes row r mod 4 of a Williams square of the four
# sides: over four rounds each side runs once at each position, and each side directly follows each other side once.
# So a drift over the take, or a carryover from the run before, falls on every side alike.
set -uo pipefail
out=$1; scene=$2; first=$3; rounds=$4  # a fifth argument --inner runs the body, under the lock
tree=$(pwd)
lock=$tree/tools/gpu-lock.sh
main_bin=/private/tmp/ter-kh4g/1b/main/Tertius.Demo
probe_bin=/private/tmp/ter-kh4g/1b/probe/Tertius.Demo
sides=(${SIDES:-main layer ten off})
n=${#sides[@]}
(( n == 4 )) || { echo "the Williams square needs four sides, not $n" >&2; exit 2; }
# The rows of a Williams square of order 4, one row per round: the index into sides at each position.
williams=(0 1 3 2  1 2 0 3  2 3 1 0  3 0 2 1)
export GPU_CALIBRATION_BIN=/private/tmp/tertius-survey-20261109/clockbin
mkdir -p "$out/$scene"
log=$out/$scene/take-$first.log

# The body of the take, which runs under the lock.
take() {
  local stops_file=$out/$scene/stops.txt
  if [[ ! -s $stops_file ]]; then
    "$main_bin" --script "scripts/$scene.json" --headless --out "$out/$scene/exposure-run" --video none \
      --in-flight 2 --pass-timestamps off > "$out/$scene/exposure-run.log" 2>&1 || { echo "exposure run failed"; return 1; }
    python3 -I -c 'import json, statistics, sys; print(statistics.median(e["stops"] for e in json.load(open(sys.argv[1]))))' \
      "$out/$scene/exposure-run/exposure.json" > "$stops_file"
  else
    "$main_bin" --script "scripts/$scene.json" --headless --out "$out/$scene/warm-up-$first" --video none \
      --in-flight 2 --pass-timestamps off --exposure "$(cat "$stops_file")" > "$out/$scene/warm-up-$first.log" 2>&1
    rm -rf "$out/$scene/warm-up-$first"
  fi
  local stops; stops=$(cat "$stops_file")
  echo "$scene stops $stops"
  for ((r = first; r < first + rounds; r++)); do
    for ((k = 0; k < n; k++)); do
      local i=${williams[$(( (r % 4) * 4 + k ))]}
      local side=${sides[$i]} bin=$probe_bin mode=
      [[ $side == main ]] && bin=$main_bin || mode=$side
      local run=$out/$scene/$side/run-$r
      rm -rf "$run"; mkdir -p "$run"
      local start; start=$(date +%s)
      TERTIUS_BACK_FACE_PROBE=$mode bash "$tree/tools/gpu-clock.sh" "$bin" --script "scripts/$scene.json" --headless \
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
