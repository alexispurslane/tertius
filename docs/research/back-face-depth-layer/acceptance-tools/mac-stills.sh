#!/usr/bin/env bash
# Tool code (t1-8b4w): the stills and the film of one view on the Mac, main and the branch, at one fixed exposure.
# Run from the worktree's root, inside the dev shell, under the GPU lock's shared mode:
#   nix develop --command bash -c 'bash tools/gpu-lock.sh --shared <log> bash <this> <out> <scene> <script> [<stops>]'
# The binaries are the Release demos of main (d8559eb58, built in a second tree) and of the branch, in
# /private/tmp/t1-8b4w/bin-main and bin-branch. Without <stops>, one run of main with the automatic exposure gives the
# stops of the film's frame 239, and both sides then run with them. Each side films 240 frames at 1920 x 1080 with bloom
# off. A still is the film's frame 239, because a capture resets the histories (stage-1b/pictures.md). The script keeps
# the frames 231 and 239 as lossless WebP in <out>/<scene>/out/pack, the layout of regions.py and crops.py. It encodes
# the film into <out>/<scene>/film-<side>.mp4 and measures the flicker of the frames 180 to 239 with films.py. Then it
# deletes the PNG frames.
set -uo pipefail
out=$1; scene=$2; script=$3; stops=${4:-}
tree=$(pwd)
tools=$tree/docs/research/back-face-depth-layer/acceptance-tools
dir=$out/$scene
mkdir -p "$dir/out/pack"
run() { # <side> <binary side> [demo arguments]
  local side=$1 bin=/private/tmp/t1-8b4w/bin-$2/Tertius.Demo; shift 2
  rm -rf "${dir:?}/$side"; mkdir -p "$dir/$side"
  "$bin" --script "$script" --headless --out "$dir/$side" --size 1920x1080 --bloom off "$@" > "$dir/$side.log" 2>&1
  echo "$scene $side exit $? load $(uptime | sed 's/.*averages*: //')" | tee -a "$dir/summary.txt"
}
if [[ -z $stops ]]; then
  run auto main --video none
  stops=$(python3 -I -c "import json,sys; print(next(e['stops'] for e in json.load(open(sys.argv[1])) if e['capture'] == 's-000239'))" "$dir/auto/exposure.json")
  rm -rf "${dir:?}/auto"
fi
echo "$scene stops $stops" | tee -a "$dir/summary.txt"
for side in main branch; do
  run "$side" "$side" --exposure "$stops"
  python3 -I -c 'import json,sys; s={round(e["stops"],6) for e in json.load(open(sys.argv[1]))}; print("exposures held:", sorted(s))' \
    "$dir/$side/exposure.json" | tee -a "$dir/summary.txt"
  for f in 000231 000239; do
    python3 -I -c 'import sys; from PIL import Image; Image.open(sys.argv[1]).convert("RGB").save(sys.argv[2], lossless=True, quality=100, method=4)' \
      "$dir/$side/s-$f.png" "$dir/out/pack/$side-$f.webp"
  done
  ffmpeg -loglevel error -y -framerate 30 -i "$dir/$side/s-%06d.png" -c:v libx264 -crf 14 -preset slow -pix_fmt yuv420p \
    "$dir/film-$side.mp4"
  python3 -I "$tools/films.py" measure "$dir/$side" 180 239 "$dir/flicker-$side.npy"
  find "$dir/$side" -name 's-*.png' -delete
done
