#!/usr/bin/env bash
# Tool code of t1-yi11. builds.sh [ya] [yc] [yg] [yh] [yi] [yj]: the probe builds into /private/tmp/t1-yi11/<name>/probe-bin, through the
# acceptance record's build.sh (git archive of the line's head 5144cbe9a, one patch, the Release demo, no build under
# 22 GiB free):
#   ya: scratch-ya.patch, today's region test with the knobs TERTIUS_X_DIFFUSE_CAP and TERTIUS_X_REGION_OFF: columns a,
#       b and d;
#   yc: scratch-yc.patch, the repaired region test of t0-qvm1-region-cap with TERTIUS_X_DIFFUSE_CAP: column c;
#   yg: scratch-yg.patch, ya with the cause gate, the signed rank test and the ring log: columns g, r and g+r;
#   yh: scratch-yh.patch, yg with a fixed turn of the camera (TERTIUS_X_YAW_DEGREES, TERTIUS_X_PITCH_DEGREES).
#   yi: scratch-yi.patch, yh with a 72-layer ring, the closed reset, w36, the pair rule and the stat log region-t.log;
#   yj: scratch-yj.patch, yi with the specified hyst rule (TERTIUS_X_REGION_CLOSED=hyst) and its trace mode.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/.." && pwd); N=$(cd "$T/../.." && pwd)
HEAD=5144cbe9a
export BASE=/private/tmp/t1-yi11; mkdir -p $BASE
for name in ${@:-ya yc}; do
  echo "$(date +%T) build $name: $(df -h /System/Volumes/Data | awk 'NR == 2 {print $4}') free"
  bash "$N/design-x-acceptance-2026-11-22/tools/build.sh" $name $HEAD $R/scratch-$name.patch > $BASE/build-$name.log 2>&1 \
    || { echo "build $name failed: $BASE/build-$name.log"; exit 1; }
done
echo "$(date +%T) builds done"
