#!/usr/bin/env bash
# Tool code of t1-af5w. stills.sh <bin> <out> <view>[@<render scale>]...: renders each view script of views/ (views.py)
# with today's plain build at 1920 x 1080 and the headless render scale 2/3 (or the scale given), the automatic exposure,
# under the GPU lock's shared mode, into <out>/<view>[@scale]/. The script's three captures a, b and c are stills of a
# frozen world, and exposure.json holds each one's exposure, so b to c shows whether it settled.
set -u
bin=$1; out=$2; shift 2
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
mkdir -p "$out"
for spec in "$@"; do
  v=${spec%@*}; scale=(); [[ $spec == *@* ]] && scale=(--render-scale "${spec#*@}")
  d="$out/$spec"; [ -e "$d/c.png" ] && continue
  /bin/rm -rf "$d"; mkdir -p "$d"
  (cd "$R" && nix develop --command bash tools/gpu-lock.sh --shared "$d/lock.log" "$bin" --script "$T/views/$v.json" \
    --headless --out "$d" --size 1920x1080 ${scale[@]+"${scale[@]}"} > "$d/run.log" 2>&1) || echo "$spec failed: $(tail -1 "$d/run.log")"
  echo "$(date +%T) $spec $(python3 -c "import json,sys; e=json.load(open(sys.argv[1])); print(' '.join(f\"{x['capture']} {x['stops']:.4f}\" for x in e))" "$d/exposure.json" 2>/dev/null)"
done
