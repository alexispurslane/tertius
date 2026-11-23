#!/usr/bin/env bash
# Tool code of t1-af5w, part 1. fixedtimes.sh <bin> <out> <repetitions> <label>=<least|main|env:A=1,B=2>... -- <view>[@scale]...:
# the timing twin of fixedstills.sh. It renders the same view scripts at the same fixed exposure, but each run holds the
# GPU lock alone (a timed run), and it interleaves the columns: repetition 1 of every label and view, then repetition 2,
# so that a drift of the machine's load spreads over every column. Each run goes to <out>/<label>/<rep>/<view>/, whose
# gpu-timings.json holds the passes' medians over the run's captures.
set -u
bin=$1; out=$2; reps=$3; shift 3
labels=(); while [ "$1" != -- ]; do labels+=("$1"); shift; done; shift
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
for rep in $(seq 1 "$reps"); do
  for entry in "${labels[@]}"; do
    label=${entry%%=*}; least=${entry#*=}
    env=()
    if [[ $least == env:* ]]; then IFS=, read -ra env <<< "${least#env:}"
    elif [ "$least" != main ]; then env=(TERTIUS_X_TILE_LEAST_RAMP="$least"); fi
    for spec in "$@"; do
      v=${spec%@*}; scale=(); [[ $spec == *@* ]] && scale=(--render-scale "${spec#*@}")
      stops=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))[0]['stops'])" "$T/../exposure/$spec.json")
      d="$out/$label/$rep/$spec"; [ -e "$d/gpu-timings.json" ] && continue
      mkdir -p "$d"
      (cd "$R" && env ${env[@]+"${env[@]}"} nix develop --command bash tools/gpu-lock.sh "$d/lock.log" "$bin" \
        --script "$T/views/$v.json" --headless --out "$d" --size 1920x1080 --exposure "$stops" ${scale[@]+"${scale[@]}"} \
        > "$d/run.log" 2>&1) || echo "$label $rep $spec failed: $(tail -1 "$d/run.log")"
      echo "$(date +%T) $label $rep $spec load $(uptime | sed 's/.*averages*: //')"
    done
  done
done
