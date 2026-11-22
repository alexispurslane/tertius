#!/usr/bin/env bash
# Tool code of t0-acxm. timing-antilag.sh <out> [first] [last]: the timing rounds a1 to a8 of the anti-lag rule, as
# timing-bay.sh: sides line (36c3fd0e8), reviewer (the built form) and settled (settled-antilag.patch), the order turned
# round each round.
out=$1; first=${2:-1}; last=${3:-8}
for r in $(seq "$first" "$last"); do
  if (( r % 2 )); then sides=(line reviewer settled); else sides=(settled reviewer line); fi
  ROUND_MAX_LOAD=8 bash "$(dirname "$0")/round.sh" "$out" "a$r" "${sides[@]}"
done
