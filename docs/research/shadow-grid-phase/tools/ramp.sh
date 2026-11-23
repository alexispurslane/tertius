#!/usr/bin/env bash
# Tool code (t1-gout, step 3). ramp.sh <bin> <label> <tick> <out.txt>: take.sh's jamb crops at the windows of the three
# teeth of the aspect error, one take each, at <tick> and the render scale $SCALE (default 2/3, the headless default).
# The windows come from the gap model of phase.py over the 181 render sizes of sizeramp.py: one or two per level of the
# model's gap across the teeth at -0.34 % and +0.45 %, the one aspect of the tooth at +1.25 %, and 16:9. Each line of
# <out.txt> is "label W H aspect-error right left". The kept frames are deleted after the measure, except at 1800 x 1008
# and 1920 x 1080 and wherever a jamb reads above 0.08.
set -u
bin=$1; label=$2; tick=$3; out=$4
T=$(cd "$(dirname "$0")" && pwd); R=$(git -C "$T" rev-parse --show-toplevel)
EV=${EV:-$R-evidence/step3/ramp}; export EV SCALE=${SCALE:-0.6667}
SIZES="1800x1016 1616x912 1744x984 1872x1056 1504x848 1760x992 1704x960 1648x928 1848x1040
1800x1008 1872x1048 1744x976 1816x1016 1888x1056 1760x984 1832x1024 1904x1064 1776x992
1800x1000 1920x1080"
for size in $SIZES; do
  line=$(bash "$T/take.sh" "$bin" "$label" "$size" "$tick")
  echo "$line" | tee -a "$out"
  set -- $line
  case "$size" in 1800x1008|1920x1080) continue ;; esac
  awk -v r="$5" -v l="$6" 'BEGIN { exit !(r <= 0.08 && l <= 0.08) }' && /bin/rm -rf "$EV/$label-$size-t$tick" "$EV/$label-$size-t$tick-opening.png"
done
