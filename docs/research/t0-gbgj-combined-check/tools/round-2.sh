#!/usr/bin/env bash
# Tool code of t0-gbgj. round-2.sh <round> <side...>: round.sh with the load check inside the lock (run-one.sh). For each
# side in turn, one run of the Release demo (a try that finds no quiet start under the lock gives the lock back, waits
# outside it until the load is under 8, and tries again, at most 24 times) of $W/bins/<side>/plain on the deck, --size 1920x1080 --in-flight 2
# --video none, under the exclusive GPU lock and beside gpu-clock, then gpu_clock.py record. Output: $W/timing/<round>-<side>/.
set -uo pipefail
. "$(dirname "$0")/vars.sh"
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
round=$1; shift
mkdir -p "$W/timing"; cd "$R"
for side in "$@"; do
  out=$W/timing/$round-$side
  for try in $(seq 24); do
    /bin/rm -rf "$out"; mkdir -p "$out"
    nix develop --command bash tools/gpu-lock.sh "$out/run.log" bash "$T/run-one.sh" "$out" "$W/bins/$side/plain/Tertius.Demo"
    code=$?
    echo "$(date +%T) $round $side try $try exit $code $(grep -E 'load-at-start|no quiet start' "$out/run.log")"
    [ "$code" = 75 ] || break
    for _ in $(seq 180); do awk -v l="$(sysctl -n vm.loadavg | awk '{print $2}')" 'BEGIN { exit !(l < 8) }' && break; sleep 10; done
  done
  [ -f "$out/gpu-timings.json" ] && nix develop --command python3 -I tools/gpu_clock.py record "$out/run.log" "$out/gpu-timings.json" > /dev/null
  find "$out" -name '*.png' -delete
done
