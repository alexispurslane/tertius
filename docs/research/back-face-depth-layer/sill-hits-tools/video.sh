#!/usr/bin/env bash
# Tool code (ter-ukw6): one side-by-side video of a view, before (c4c1f1f2f) on the left and after (821481a5c) on the
# right, real frames at 60 a second, at one fixed exposure, H.264 at CRF 12.
# Run from the worktree's root, inside the dev shell, under the GPU lock's shared mode:
#   bash tools/gpu-lock.sh --shared <log> bash <this> <out> <script> <stops> <first frame> <crop x0,y0,x1,y1>
# The script films 240 frames named s at 1920 x 1080 with bloom off. The video holds the frames <first frame> to 239,
# each side cropped to the output pixels of <crop>, with a label in its corner. It writes <out>/side-by-side.mp4 and
# deletes the frames.
set -uo pipefail
out=$1; script=$2; stops=$3; first=$4; crop=$5
mkdir -p "$out/frames"
for side in before after; do
  rm -rf "${out:?}/$side"; mkdir -p "$out/$side"
  /private/tmp/ter-ukw6/bin-$side/Tertius.Demo --script "$script" --headless --out "$out/$side" --size 1920x1080 --bloom off \
    --exposure "$stops" > "$out/$side.log" 2>&1
  echo "$side exit $? load $(uptime | sed 's/.*averages*: //')" | tee -a "$out/summary.txt"
done
python3 -I - "$out" "$first" "$crop" <<'PY'
import sys
from PIL import Image, ImageDraw
out, first, crop = sys.argv[1], int(sys.argv[2]), tuple(int(v) for v in sys.argv[3].split(","))
w, h = crop[2] - crop[0], crop[3] - crop[1]
for n, f in enumerate(range(first, 240)):
    sheet = Image.new("RGB", (2 * w + 8, h), "black")
    for i, side in enumerate(("before", "after")):
        sheet.paste(Image.open(f"{out}/{side}/s-{f:06d}.png").convert("RGB").crop(crop), (i * (w + 8), 0))
        ImageDraw.Draw(sheet).text((i * (w + 8) + 10, h - 24), f"{side} ({'c4c1f1f2f' if i == 0 else '821481a5c'})", fill="yellow")
    sheet.save(f"{out}/frames/v-{n:06d}.png")
PY
ffmpeg -loglevel error -y -framerate 60 -i "$out/frames/v-%06d.png" -c:v libx264 -crf 12 -preset slow -pix_fmt yuv420p "$out/side-by-side.mp4"
echo "video exit $?" | tee -a "$out/summary.txt"
rm -rf "${out:?}/frames"; find "$out/before" "$out/after" -name 's-*.png' ! -name 's-000239.png' -delete
