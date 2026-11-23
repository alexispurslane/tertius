#!/usr/bin/env bash
# pics.sh: the pictures of pics/ from the run folders in /private/tmp/t0-xlcu/runs, through m.sh (the dev shell). The
# chart of r11 over the cabin switch under today's jitter, in the style of t0-mzhr's levelchart.py, and the crop sheet
# of the lamp hall's ceiling (output pixels 640 to 1280 by 0 to 420, the shown still at the automatic exposure).
set -euo pipefail
M=$(dirname "$0")/m.sh; P=$(cd "$(dirname "$0")/.." && pwd)/pics; mkdir -p "$P"
S=results/switch/cabin.json
"$M" levelchart.py "$P/cabin-r11-today.png" s/t-A-sw/$S "reference=s/t-A-sw/$S" "fix=s/t-fix-sw/$S" "T0=s/t-T0-sw/$S" \
  "T1=s/t-T1-sw/$S" "T2=s/t-T2-sw/$S"
"$M" sheet.py "$P/rect-lamps-ceiling-today.png" rect-lamps 640,0,1280,420 1 "fix=t-fix-s" "T0=t-T0-s" "T1=t-T1-s" "T2=t-T2-s"
