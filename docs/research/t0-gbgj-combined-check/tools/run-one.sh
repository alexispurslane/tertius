#!/usr/bin/env bash
# Tool code of t0-gbgj. run-one.sh <out> <demo>: runs inside tools/gpu-lock.sh's exclusive lock (round-2.sh starts it). It
# first waits, under the lock, until the machine's load over one minute is under 8, for at most 5 minutes, so that the
# load check holds when the run starts (a wait for the lock itself took up to 632 s, and the load rose to 38 meanwhile).
# If the load stays at 8 or more, it exits with code 75 without a run and so gives the lock back (round-2.sh tries again
# later). Else it runs the demo on the deck in the form of tools/demo-timing.sh, beside gpu-clock. The load at the run's
# start goes into the log as "load-at-start".
set -uo pipefail
out=$1; demo=$2
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
quiet=0
for _ in $(seq 60); do
  awk -v l="$(sysctl -n vm.loadavg | awk '{print $2}')" 'BEGIN { exit !(l < 8) }' && { quiet=1; break; }
  sleep 5
done
[ "$quiet" = 1 ] || { echo "no quiet start: $(sysctl -n vm.loadavg)"; exit 75; }
echo "load-at-start: $(sysctl -n vm.loadavg)"
bash "$R/tools/gpu-clock.sh" "$demo" --script "$R/scripts/temporal-upscale-timing.json" --headless --out "$out" \
  --video none --size 1920x1080 --in-flight 2
