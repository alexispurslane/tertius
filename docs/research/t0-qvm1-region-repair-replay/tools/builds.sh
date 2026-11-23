#!/usr/bin/env bash
# Tool code of the region repair's replay (t0-qvm1). builds.sh [name...]: the two probe builds of the replay's runs, each
# from main 165a964e7 with the survey's probe patch, ../scratch-history-dump.patch (the slow diffuse history's length at
# listed draws) and ../scratch-region-log.patch (t0-3vwx's region log: per draw and anchor the region's light, its count of
# valid draws and the test's answer), through the acceptance record's build.sh (git archive, the patches in order with
# GNU patch, the Release demo into /private/tmp/t0-5tyj/<name>/probe-bin, no build under 22 GiB free). One at a time.
#   r2-main:  main as it is.
#   r2-noreg: region evidence off (../scratch-no-region-evidence.patch: the drift test runs and logs, its evidence caps
#             nothing).
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd)
B=$(cd "$N/../design-x-acceptance-2026-11-22/tools" && pwd)
MAIN=165a964e7
base="$MAIN:tools/renderer-survey/probe.patch $N/scratch-history-dump.patch $N/scratch-region-log.patch"
for name in ${@:-r2-main r2-noreg}; do
  case $name in
    r2-main) extra= ;;
    r2-noreg) extra=$N/scratch-no-region-evidence.patch ;;
    *) echo "no build $name"; exit 1 ;;
  esac
  [ -x /private/tmp/t0-5tyj/$name/probe-bin/Tertius.Demo ] && continue
  echo "$(date +%T) build $name: $(df -h /System/Volumes/Data | awk 'NR == 2 {print $4}') free"
  bash "$B/build.sh" $name $MAIN $base $extra || exit 1
done
