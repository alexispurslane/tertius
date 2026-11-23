#!/usr/bin/env bash
# Tool code of t1-af5w. probe.sh <bin> <out> [view...]: runs each view script of views/ (views.py) with the shadow-edge
# probe build (edge-probe.patch: the direct pass writes each ramp pixel's gap, rendered width and light), at 1920 x 1080
# and the headless render scale 2/3, under the GPU lock's shared mode, into <out>/<view>/. It keeps the probe's files
# (edge-a.f32, edge-b.f32, edge.txt: the last draw, capture c) and exposure.json, and deletes the pictures, which do not
# show the light.
set -u
bin=$1; out=$2; shift 2
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
views=${*:-$(cd $T/views && ls *.json | sed 's/\.json$//')}
mkdir -p "$out"
for v in $views; do
  [ -e "$out/$v/edge.txt" ] && continue
  /bin/rm -rf "$out/$v"; mkdir -p "$out/$v"
  (cd "$R" && TERTIUS_X_EDGE_PROBE="$out/$v" nix develop --command bash tools/gpu-lock.sh --shared "$out/$v/lock.log" \
    "$bin" --script "$T/views/$v.json" --headless --out "$out/$v" --size 1920x1080 --bloom off > "$out/$v/run.log" 2>&1) \
    || echo "$v failed: $(tail -1 "$out/$v/run.log")"
  /bin/rm -f "$out/$v"/*.png
  echo "$(date +%T) $v $(cat "$out/$v/edge.txt" 2>/dev/null)"
done
