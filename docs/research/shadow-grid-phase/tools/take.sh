#!/usr/bin/env bash
# Tool code (t1-gout). take.sh <bin> <label> <W>x<H> <tick> [demo flags...]: the window test's view (scene orbit, camera
# person, the lamps on) in a window of W x H at the render scale $SCALE (default 1) and 4.08 stops, with the kept frame at <tick>: the run
# goes to <tick> - 40, takes the still, then a film of 40 draws, one tick each. Environment variables pass through, such
# as the scratch probe's TERTIUS_X_* toggles (scratch-toggles.patch). It keeps the film's last frame and the still, under
# $EV/<label>-<W>x<H>-t<tick>, prints the jamb crops of wintest.py, and writes the opening at 2x beside the folder.
set -u
bin=$1; label=$2; size=$3; tick=$4; shift 4
w=${size%x*}; h=${size#*x}
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
EV=${EV:-$R-evidence/gout}
out=$EV/$label-$size-t$tick; /bin/rm -rf "$out"; mkdir -p "$out"
python3 -I - "$out" "$w" "$h" "$((tick - 40))" <<'PY'
import json, sys
out, w, h, ticks = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
json.dump({'scene': 'orbit', 'window': {'width': w, 'height': h}, 'steps': [{'do': 'camera', 'entity': 'person'},
  {'do': 'run', 'ticks': ticks}, {'do': 'capture', 'name': 'still'}, {'do': 'film', 'name': 'f', 'frames': 40, 'every': 1}]},
  open(f'{out}/script.json', 'w'), indent=1)
PY
for try in 1 2 3; do
  nix develop "$R" --command bash "${GPU_LOCK:-/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh}" --shared "$out/lock.log" "$bin" \
    --script "$out/script.json" --headless --out "$out/frames" --size "$size" --render-scale "${SCALE:-1}" --exposure 4.08 "$@" \
    > "$out/run.log" 2>&1 && break
  grep -q ErrorIncompatibleDriver "$out/run.log" || break
done
find "$out/frames" -name 'f-*.png' ! -name 'f-000039.png' -delete
echo "$label $(nix develop "$R" --command python3 -I "$T/wintest.py" measure "$out" "$w" "$h" 2>/dev/null | tail -1)"
cp "$out/opening.png" "$out-opening.png" 2>/dev/null
