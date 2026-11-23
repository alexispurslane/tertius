#!/usr/bin/env bash
# Tool code of t1-af5w, part 0 on. fixedstills.sh <bin> <out> <label> <least ramp or main> <view>[@<render scale>]...:
# renders each view script of views/ (views.py) at 1920 x 1080 with the headless render scale 2/3 (or the scale given)
# and the fixed exposure of capture a in ../exposure/<view>.json, under the GPU lock's shared mode, into
# <out>/<label>/<view>[@scale]/. The least ramp goes to the probe build as TERTIUS_X_TILE_LEAST_RAMP: positive with the
# trust rule on, negative with it off; "main" leaves it unset, which draws main's shadows.
set -u
bin=$1; out=$2; label=$3; least=$4; shift 4
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
for spec in "$@"; do
  v=${spec%@*}; scale=(); [[ $spec == *@* ]] && scale=(--render-scale "${spec#*@}")
  stops=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))[0]['stops'])" "$T/../exposure/$spec.json")
  d="$out/$label/$spec"; [ -e "$d/a.png" ] && continue
  mkdir -p "$d"
  env=(); [ "$least" != main ] && env=(TERTIUS_X_TILE_LEAST_RAMP="$least")
  (cd "$R" && env ${env[@]+"${env[@]}"} nix develop --command bash tools/gpu-lock.sh --shared "$d/lock.log" "$bin" \
    --script "$T/views/$v.json" --headless --out "$d" --size 1920x1080 --exposure "$stops" ${scale[@]+"${scale[@]}"} \
    > "$d/run.log" 2>&1) || echo "$label $spec failed: $(tail -1 "$d/run.log")"
  echo "$(date +%T) $label $spec stops $stops"
done
