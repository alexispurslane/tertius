#!/usr/bin/env bash
# Tool code (ter-ukw6): the stills of one view, before (c4c1f1f2f) against after (821481a5c), at one fixed exposure.
# acceptance-tools/mac-stills.sh with this ticket's binaries and without the film's video and flicker.
# Run from the worktree's root, inside the dev shell, under the GPU lock's shared mode:
#   bash tools/gpu-lock.sh --shared <log> bash <this> <out> <view> <script> [<stops>]
# The binaries are the plain Release demos in /private/tmp/ter-ukw6/bin-before and bin-after. Without <stops>, one run
# of before with the automatic exposure gives the stops of the film's frame 239, and both sides then run with them.
# The script films 240 frames named s. Each side runs at 1920 x 1080 with bloom off. (--video none writes no frames.) A still is the film's frame 239,
# because a capture resets the histories (stage-1b/pictures.md). The script keeps the frames 231 and 239 as lossless
# WebP in <out>/<view>/out/pack, the layout of acceptance-tools/regions.py and crops.py, with before as the base
# column "main" and after as "branch". It deletes the other frames.
set -uo pipefail
out=$1; view=$2; script=$3; stops=${4:-}
dir=$out/$view
mkdir -p "$dir/out/pack"
run() { # <side> [demo arguments]
  local side=$1 bin=/private/tmp/ter-ukw6/bin-$1/Tertius.Demo; shift
  rm -rf "${dir:?}/$side"; mkdir -p "$dir/$side"
  "$bin" --script "$script" --headless --out "$dir/$side" --size 1920x1080 --bloom off "$@" > "$dir/$side.log" 2>&1
  echo "$view $side exit $? load $(uptime | sed 's/.*averages*: //')" | tee -a "$dir/summary.txt"
}
if [[ -z $stops ]]; then
  run before
  stops=$(python3 -I -c "import json,sys; print(next(e['stops'] for e in json.load(open(sys.argv[1])) if e['capture'] == 's-000239'))" "$dir/before/exposure.json")
  rm -rf "${dir:?}/before"
fi
echo "$view stops $stops" | tee -a "$dir/summary.txt"
for side in before after; do
  run "$side" --exposure "$stops"
  python3 -I -c 'import json,sys; s={round(e["stops"],6) for e in json.load(open(sys.argv[1]))}; print("exposures held:", sorted(s))' \
    "$dir/$side/exposure.json" | tee -a "$dir/summary.txt"
  pack=main; [[ $side == after ]] && pack=branch
  for f in 000231 000239; do
    python3 -I -c 'import sys; from PIL import Image; Image.open(sys.argv[1]).convert("RGB").save(sys.argv[2], lossless=True, quality=100, method=4)' \
      "$dir/$side/s-$f.png" "$dir/out/pack/$pack-$f.webp"
  done
  find "$dir/$side" -name 's-*.png' ! -name 's-000239.png' -delete
done
