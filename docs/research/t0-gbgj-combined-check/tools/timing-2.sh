#!/usr/bin/env bash
# Tool code of t0-gbgj. timing-2.sh <first round> <last round>: timing.sh with round-2.sh, the load check inside the lock.
# The order of the sides is t0-89zi's: odd rounds M2 then X2, even rounds X2 then M2. A round whose two runs exist is kept.
T=$(cd "$(dirname "$0")" && pwd); . "$T/vars.sh"
mkdir -p "$W/timing"
for r in $(seq "$1" "$2"); do
  if [ -f "$W/timing/r$r-m2/gpu-timings.json" ] && [ -f "$W/timing/r$r-x2/gpu-timings.json" ]; then echo "kept r$r"; continue; fi
  echo "r$r before: $(uptime)" | tee -a "$W/timing/loads.txt"
  if [ $((r % 2)) = 1 ]; then bash "$T/round-2.sh" "r$r" m2 x2; else bash "$T/round-2.sh" "r$r" x2 m2; fi
  echo "r$r after: $(uptime)" | tee -a "$W/timing/loads.txt"
done
