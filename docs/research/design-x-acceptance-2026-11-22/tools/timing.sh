#!/usr/bin/env bash
# Tool code of t0-5tyj. timing.sh [first round] [last round]: take T17, the GPU frame span of M and X, rounds t1 to t3 by
# default, each under the exclusive GPU lock (round.sh) after the load over one minute falls below 8. Each round runs the
# deck (scripts/temporal-upscale-timing.json) and the film rect-bay (rect-bay-film.json, filmscript.py) on both sides.
# Sides, folders of $BASE/t17/bin: main (the plain Release demo of c4c1f1f2f, build tm of queue-mac2.sh) and build (the
# plain Release demo of the line's head, build tx). The order of the sides turns round each round. table.py prints the
# paired differences: bash m.sh table.py ../t17/timing main build (from $BASE/w).
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd)
# MAIN and BUILD name the builds of the two sides in $BASE (default tm and tx), and OUT the take's folder (default t17).
BASE=${BASE:-/private/tmp/t0-5tyj}; out=$BASE/${OUT:-t17}; first=${1:-1}; last=${2:-3}
mkdir -p "$out/bin"
ln -sfn "$BASE/${MAIN:-tm}/probe-bin" "$out/bin/main"; ln -sfn "$BASE/${BUILD:-tx}/probe-bin" "$out/bin/build"
[ -f "$out/rect-bay-film.json" ] || (cd "$R" && nix develop --command python3 "$T/filmscript.py" "$out/rect-bay-film.json" rect-bay)
for r in $(seq "$first" "$last"); do
  if (( r % 2 )); then sides=(main build); else sides=(build main); fi
  ROUND_MAX_LOAD=8 bash "$T/round.sh" "$out" "t$r" "${sides[@]}"
done
