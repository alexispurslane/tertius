#!/usr/bin/env bash
# Tool code of t0-acxm. timing-bay.sh <out> [first round] [last round]: the timing rounds of the bay takes, b1 to b8 by
# default, each under the exclusive GPU lock (round.sh) after the load over one minute falls below 8. Sides, folders of
# <out>/bin: main (c4c1f1f2f), build (the merged head), buildsw1 (the build with the region's test for the bounce light
# only) and cut6 (the scratch cut to 6 draws). The order of the sides turns round each round, so that no side always
# runs first.
out=$1; first=${2:-1}; last=${3:-8}
for r in $(seq "$first" "$last"); do
  if (( r % 2 )); then sides=(main build buildsw1 cut6); else sides=(cut6 buildsw1 build main); fi
  ROUND_MAX_LOAD=8 bash "$(dirname "$0")/round.sh" "$out" "b$r" "${sides[@]}"
done
