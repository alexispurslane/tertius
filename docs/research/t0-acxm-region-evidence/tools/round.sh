#!/usr/bin/env bash
# Tool code of t0-acxm. round.sh <out> <round> <side...>: one timing round. It takes the exclusive GPU lock once
# (tools/gpu-lock.sh without --shared) and runs round-inner.sh under it, which runs each side alone, one after the other,
# beside gpu-clock (tools/gpu-clock.sh): the deck (scripts/temporal-upscale-timing.json) and the film rect-bay. A side is a
# folder name under <out>/bin, which holds a Release demo built with -o. The film's script is <out>/rect-bay-film.json
# (docs/research/t0-xdck-drift-cut-cost/tools/filmscript.py). Output: <out>/timing/<round>-<side>-<deck|film>/.
# If ROUND_MAX_LOAD is set, the round first waits until the machine's load over one minute is below it, for at most
# 30 minutes. round-inner.sh logs the load before each run.
set -uo pipefail
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
out=$1; round=$2; shift 2
mkdir -p "$out/timing"
cd "$R"
if [ -n "${ROUND_MAX_LOAD:-}" ]; then
  for _ in $(seq 180); do
    load=$(sysctl -n vm.loadavg | awk '{print $2}')
    awk -v l="$load" -v m="$ROUND_MAX_LOAD" 'BEGIN { exit !(l < m) }' && break
    sleep 10
  done
fi
echo "$(date +%T) start round $round: $* (load $(sysctl -n vm.loadavg))"
nix develop --command bash tools/gpu-lock.sh "$out/timing/$round.log" bash "$(dirname "$0")/round-inner.sh" "$out" "$round" "$@"
echo "$(date +%T) end round $round exit $?"
