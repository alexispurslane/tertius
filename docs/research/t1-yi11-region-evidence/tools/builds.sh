#!/usr/bin/env bash
# Tool code of t1-yi11. builds.sh [ya] [yc]: the probe builds into /private/tmp/t1-yi11/<name>/probe-bin, through the
# acceptance record's build.sh (git archive of the line's head 5144cbe9a, one patch, the Release demo, no build under
# 22 GiB free):
#   ya: scratch-ya.patch, today's region test with the knobs TERTIUS_X_DIFFUSE_CAP and TERTIUS_X_REGION_OFF: columns a,
#       b and d;
#   yc: scratch-yc.patch, the repaired region test of t0-qvm1-region-cap with TERTIUS_X_DIFFUSE_CAP: column c.
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
