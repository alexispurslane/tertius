#!/usr/bin/env bash
# Tool code of t0-acxm, copied unchanged for t0-5tyj. round-inner.sh <out> <round> <side...>: runs under round.sh's GPU lock. For each side, one headless
# run of the deck and one of the film rect-bay, at 1920 x 1080 with two frames in flight and --video none, beside
# gpu-clock. The record of the clock goes into each run's gpu-timings.json (tools/gpu_clock.py record).
set -uo pipefail
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
out=$1; round=$2; shift 2
for side in "$@"; do
  for script in deck film; do
    run=$out/timing/$round-$side-$script
    rm -rf "$run"; mkdir -p "$run"
    if [ "$script" = deck ]; then file=$R/scripts/temporal-upscale-timing.json; else file=$out/rect-bay-film.json; fi
    echo "$(date +%T) $side $script $(uptime)"
    bash "$R/tools/gpu-clock.sh" "$out/bin/$side/Tertius.Demo" --script "$file" --headless --out "$run" --video none \
      --size 1920x1080 --in-flight 2 > "$run/run.log" 2>&1
    echo "$(date +%T) $side $script exit $?"
    [ -f "$run/gpu-timings.json" ] && python3 -I "$R/tools/gpu_clock.py" record "$run/run.log" "$run/gpu-timings.json" > /dev/null
    find "$run" -name '*.png' -delete
  done
done
