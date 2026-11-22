#!/usr/bin/env bash
# The pictures of pics/, from the result folders in /private/tmp/t0-mzhr/runs. Run it inside the dev shell. w/s/ holds a
# symbolic link per run folder (sheet.py reads s/<variant>/images/still/...); t-r4-* point at the control runs c-r8-*.
set -euo pipefail
cd /private/tmp/t0-mzhr/w
mkdir -p s; for d in ../runs/*; do n=$(basename "$d"); [ -e "s/$n" ] || ln -s "../$d" "s/$n"; done
ln -sfn ../../runs/c-r8-s s/t-r4-s; ln -sfn ../../runs/c-r8-e s/t-r4-e
L=(
  "reference (long mean)=t-ref-s:reference" "fix=t-fix-s" "region, window 4=t-r4-s" "region, window 18=t-r18-s")
python3 ../tools/sheet.py crop-ceiling.png rect-lamps 640,0,1280,420 1 "${L[@]}"
python3 ../tools/sheet.py crop-wall.png rect-lamps 0,120,560,720 1 "${L[@]}"
R=../runs
python3 ../tools/levelchart.py chart-today.png $R/t-ref-sw/results/switch/cabin.json "reference=$R/t-ref-sw/results/switch/cabin.json" \
  "main=$R/t-main-sw/results/switch/cabin.json" "fix=$R/t-fix-sw/results/switch/cabin.json" "cut 18=$R/t-c18-sw/results/switch/cabin.json" \
  "region, window 4=$R/c-r8-sw/results/switch/cabin.json" "region, window 18=$R/t-r18-sw/results/switch/cabin.json"
for j in n l; do
  name=$([ $j = n ] && echo no-jitter || echo long-sequence)
  python3 ../tools/levelchart.py chart-$name.png $R/$j-ref-sw/results/switch/cabin.json "reference=$R/$j-ref-sw/results/switch/cabin.json" \
    "fix=$R/$j-fix-sw/results/switch/cabin.json" "region, window 4=$R/$j-r4-sw/results/switch/cabin.json" \
    "region, window 18=$R/$j-r18-sw/results/switch/cabin.json"
done
# With a folder as the first argument, copies the pictures there under the research folder's names.
if [ $# -ge 1 ]; then
  cp crop-ceiling.png "$1/rect-lamps-ceiling-today.png"; cp crop-wall.png "$1/rect-lamps-wall-today.png"
  cp chart-today.png "$1/cabin-r11-today.png"; cp chart-no-jitter.png "$1/cabin-r11-no-jitter.png"
  cp chart-long-sequence.png "$1/cabin-r11-long-sequence.png"
fi
