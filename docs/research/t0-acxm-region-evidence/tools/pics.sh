#!/usr/bin/env bash
# Tool code of t0-acxm. pics.sh: the pictures of pics/ from the takes in $W/runs, through m.sh (the dev shell), for the
# sides of the final record: main (M), the line without the remedy (L) and the built form (R). The sheet of the film
# rect-bay's middle and last frames (t0-ion0's filmsheet.py) for the still, M, L and R; the 1:1 crop of the film's last
# frame on the floor above the hole (filmcrop.py); and the 1:1 crop sheets of the stills bay-sunbeam (floor, output
# pixels 480 to 980 by 520 to 1020) and exit-cabin (full image at 0.35 scale), beside the clamps-off reference first
# (t0-mzhr's sheet.py).
set -euo pipefail
M=$(dirname "$0")/m.sh; P=$(cd "$(dirname "$0")/.." && pwd)/pics; mkdir -p "$P"
"$M" filmsheet.py "$P/rect-bay-frames.png" rect-bay 0.4 "still=t-M-rb:still" "M=t-M-rb" "L=t-L-rb" "R=t-R-rb"
"$M" filmcrop.py "$P/rect-bay-end-floor.png" end 700,600,1100,1000 "still=t-M-rb:still" "M=t-M-rb" "L=t-L-rb" "R=t-R-rb"
"$M" sheet.py "$P/bay-sunbeam-floor.png" bay-sunbeam 480,520,980,1020 1 "reference=t-A-s:reference" "M=t-M-s" "L=t-L-s" "R=t-R-s"
"$M" sheet.py "$P/exit-cabin.png" exit-cabin full 0.35 "reference=t-A-s:reference" "M=t-M-s" "L=t-L-s" "R=t-R-s"
