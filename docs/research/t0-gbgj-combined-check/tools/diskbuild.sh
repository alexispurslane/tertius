#!/usr/bin/env bash
# Tool code of t0-gbgj. diskbuild.sh <log> <build.sh arguments...>: build.sh with MIN_GIB=32, beside a sampler of the free
# disk every 20 s. The log gets the free disk before, each sample, the lowest sample, and the free disk after (GiB).
# The sampler stops the build if the free disk falls under 30 GiB.
set -uo pipefail
log=$1; shift; T=$(cd "$(dirname "$0")" && pwd)
free() { df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}'; }
echo "before $(free) $(date +%T)" > "$log"
MIN_GIB=32 bash "$T/build.sh" "$@" >> "$log" 2>&1 & pid=$!
low=$(free)
while kill -0 $pid 2>/dev/null; do
  f=$(free); echo "sample $f $(date +%T)" >> "$log"
  low=$(awk -v a="$f" -v b="$low" 'BEGIN { print (a < b) ? a : b }')
  if awk -v a="$f" 'BEGIN { exit !(a < 30) }'; then echo "under 30 GiB: stopping the build" >> "$log"; pkill -P $pid; kill $pid; fi
  sleep 20
done
wait $pid; code=$?
echo "lowest $low"  >> "$log"; echo "after $(free) $(date +%T) exit $code" >> "$log"
exit $code
