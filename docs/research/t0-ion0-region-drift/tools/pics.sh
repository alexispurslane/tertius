#!/usr/bin/env bash
# pics.sh: the pictures of pics/ from the run folders in /private/tmp/t0-ion0/runs, through m.sh (the dev shell). The
# chart of r11 over the cabin switch under today's jitter (t0-mzhr's levelchart.py), the sheet of the film rect-bay's
# middle and last frames (filmsheet.py), and the crop sheet of the lamp hall's ceiling (t0-mzhr's sheet.py, output
# pixels 640 to 1280 by 0 to 420, the shown still at the automatic exposure).
set -euo pipefail
M=$(dirname "$0")/m.sh; P=$(cd "$(dirname "$0")/.." && pwd)/pics; mkdir -p "$P"
S=results/switch/cabin.json
"$M" levelchart.py "$P/cabin-r11-today.png" s/t-A-sw/$S "reference=s/t-A-sw/$S" "fix=s/t-fix-sw/$S" "cap 4=s/t-cap4-sw/$S" \
  "Q0=s/t-Q0-sw/$S" "Q1=s/t-Q1-sw/$S" "Q2=s/t-Q2-sw/$S"
"$M" filmsheet.py "$P/rect-bay-frames.png" rect-bay 0.4 "still=t-fix-rb:still" "fix=t-fix-rb" "Q1=t-Q1-rb" "Q2=t-Q2-rb" "cap 4=t-cap4-rb"
"$M" sheet.py "$P/rect-lamps-ceiling-today.png" rect-lamps 640,0,1280,420 1 "fix=t-fix-s" "Q0=t-Q0-s" "Q1=t-Q1-s" "Q2=t-Q2-s"
# The strips check: the reference's lamp hall still at 4.08 stops, today's jitter, with and without the sun.
"$M" sheet.py "$P/rect-lamps-sun-nosun.png" rect-lamps full 0.4 "with the sun=s-t-A-sun-e" "without the sun=s-t-A-nosun-e"
