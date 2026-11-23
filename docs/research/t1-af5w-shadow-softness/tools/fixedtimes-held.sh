#!/usr/bin/env bash
# Tool code of t1-af5w, part 1 (decision 111 of the line, 2026-11-23). fixedtimes-held.sh <out> <repetitions>
# <label>=<bin>=<A=1,B=2 or none>... -- <view>[@scale]...: the runs of fixedtimes.sh, each column with its own binary,
# all under ONE exclusive hold of the GPU lock, so that a take waits once in the queue and not once per run. It runs
# itself again inside tools/gpu-lock.sh and the dev shell, then renders repetition 1 of every label and view, then
# repetition 2, and so on, skipping runs whose gpu-timings.json exists. Before each run it writes the time and uptime
# into <out>/held.log, so that each run's load is on record. Each run goes to <out>/<label>/<rep>/<view>/.
set -u
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
if [ "${TERTIUS_GPU_LOCK_HELD:-}" != exclusive ]; then
  mkdir -p "$1"
  cd "$R" && exec nix develop --command bash tools/gpu-lock.sh "$1/lock.log" bash "$T/fixedtimes-held.sh" "$@"
fi
out=$1; reps=$2; shift 2
columns=(); while [ "$1" != -- ]; do columns+=("$1"); shift; done; shift
for rep in $(seq 1 "$reps"); do
  for column in "${columns[@]}"; do
    IFS== read -r label bin vars <<< "$column"
    env=(); [ "$vars" != none ] && IFS=, read -ra env <<< "$vars"
    for spec in "$@"; do
      v=${spec%@*}; scale=(); [[ $spec == *@* ]] && scale=(--render-scale "${spec#*@}")
      stops=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))[0]['stops'])" "$T/../exposure/$spec.json")
      d="$out/$label/$rep/$spec"; [ -e "$d/gpu-timings.json" ] && continue
      mkdir -p "$d"
      echo "$(date +%T) $label $rep $spec before: $(uptime | sed 's/.*averages*: //')" >> "$out/held.log"
      env ${env[@]+"${env[@]}"} "$bin" --script "$T/views/$v.json" --headless --out "$d" --size 1920x1080 \
        --exposure "$stops" ${scale[@]+"${scale[@]}"} > "$d/run.log" 2>&1 || echo "$label $rep $spec failed" >> "$out/held.log"
    done
  done
done
echo "$(date +%T) end: $(uptime | sed 's/.*averages*: //')" >> "$out/held.log"
