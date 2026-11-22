#!/usr/bin/env bash
# Tool code of t0-gbgj. timing.sh [first round] [last round]: the deck's timing rounds, by round.sh, in t0-89zi's order of
# the sides: round 1 M2 then X2, round 2 X2 then M2, round 3 M2 then X2 (main, head, head, main, main, head), and round 4
# on and so on alternate, for a round that is repeated. Before each round it waits until the machine's load over one
# minute is under 8, for at most 60 minutes, and logs the uptime into $W/timing/loads.txt. A round whose two runs exist
# is kept.
T=$(cd "$(dirname "$0")" && pwd); . "$T/vars.sh"
mkdir -p "$W/timing"
for r in $(seq "${1:-1}" "${2:-3}"); do
  if [ -f "$W/timing/r$r-m2/gpu-timings.json" ] && [ -f "$W/timing/r$r-x2/gpu-timings.json" ]; then echo "kept r$r"; continue; fi
  for _ in $(seq 360); do
    load=$(sysctl -n vm.loadavg | awk '{print $2}')
    awk -v l="$load" 'BEGIN { exit !(l < 8) }' && break
    sleep 10
  done
  echo "r$r before: $(uptime)" | tee -a "$W/timing/loads.txt"
  if [ $((r % 2)) = 1 ]; then bash "$T/round.sh" "r$r" m2 x2; else bash "$T/round.sh" "r$r" x2 m2; fi
  echo "r$r after: $(uptime)" | tee -a "$W/timing/loads.txt"
done
