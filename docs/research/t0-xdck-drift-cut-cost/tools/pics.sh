#!/usr/bin/env bash
# pics.sh: the pictures of pics/ from the run folders in /private/tmp/t0-xdck/runs, through m.sh (the dev shell): the
# sheet of the film rect-bay's middle and last frames (t0-ion0's filmsheet.py), and the crop sheet of the still
# bay-sunbeam's floor at 1:1, output pixels 480 to 980 by 520 to 1020 (t0-mzhr's sheet.py). The 1:1 crop of the film's
# last frame (rect-bay-end-floor.png, output pixels 700 to 1100 by 600 to 1000) and the span chart come from
# floorcrop.py and spanchart.py.
set -euo pipefail
M=$(dirname "$0")/m.sh; P=$(cd "$(dirname "$0")/.." && pwd)/pics; mkdir -p "$P"
"$M" filmsheet.py "$P/rect-bay-frames.png" rect-bay 0.4 "still=t-M-rb:still" "M=t-M-rb" "D4=t-D4-rb" "D8=t-D8-rb" "D4s=t-D4s-rb"
"$M" sheet.py "$P/bay-sunbeam-floor.png" bay-sunbeam 480,520,980,1020 1 "reference=t-A-s:reference" "M=t-M-s" "X=t-X-s" \
  "D4=t-D4-s" "D8=t-D8-s" "D4s=t-D4s-s"
"$M" floorcrop.py "$P/rect-bay-end-floor.png"
