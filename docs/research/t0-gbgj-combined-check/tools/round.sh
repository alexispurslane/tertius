#!/usr/bin/env bash
# Tool code of t0-gbgj. round.sh <round> <side...>: one timing round of the deck. For each side in turn, one run in the form
# of tools/demo-timing.sh, which t0-89zi used: the Release demo of $W/bins/<side>/plain (built.sh with PLAIN=1, so that the
# round builds nothing), --script scripts/temporal-upscale-timing.json --headless --video none --size 1920x1080
# --in-flight 2, under the exclusive GPU lock (tools/gpu-lock.sh without --shared) and beside gpu-clock
# (tools/gpu-clock.sh), then gpu_clock.py record. demo-timing.sh itself builds the demo of the checkout that it runs in,
# and M2 has no checkout here. Output: $W/timing/<round>-<side>/. The load goes into $W/timing/loads.txt before each run.
set -uo pipefail
. "$(dirname "$0")/vars.sh"
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
round=$1; shift
mkdir -p "$W/timing"; cd "$R"
for side in "$@"; do
  out=$W/timing/$round-$side; /bin/rm -rf "$out"; mkdir -p "$out"
  echo "$round $side $(date +%T) $(sysctl -n vm.loadavg)" >> "$W/timing/loads.txt"
  nix develop --command bash tools/gpu-lock.sh "$out/run.log" bash tools/gpu-clock.sh "$W/bins/$side/plain/Tertius.Demo" \
    --script scripts/temporal-upscale-timing.json --headless --out "$out" --video none --size 1920x1080 --in-flight 2
  echo "$(date +%T) $round $side exit $?"
  [ -f "$out/gpu-timings.json" ] && nix develop --command python3 -I tools/gpu_clock.py record "$out/run.log" "$out/gpu-timings.json" > /dev/null
  find "$out" -name '*.png' -delete
done
