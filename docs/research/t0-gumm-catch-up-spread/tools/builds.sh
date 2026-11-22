#!/usr/bin/env bash
# Tool code of t0-gumm. builds.sh [name...]: the probe builds of the experiment, into /private/tmp/t0-gbgj/bins/<name>/probe-bin,
# through t0-gbgj's build.sh (git archive of the tree, the patches in order, the Release demo without the analyzers).
# A build starts at 22 GiB free or more (the line orchestrator's limit); its log goes to /private/tmp/t0-gbgj/logs/build-<name>.log.
#   xt: today's code with the own-ray log: X5 with its probe patch, t0-3vwx's region log, dynamic scale and history log,
#       and this record's scratch-ownray-log.patch and scratch-history-dump.patch.
#   xg: X5, the tree e232080f of the line's merge 710834cc0 (Design X, light evidence restarting at 4 draws), with its
#       probe patch, t0-3vwx's region log, dynamic scale and history log, and this record's scratch-ownray-log.patch and
#       scratch-catch-up-share.patch.
#       The catch-up share's shape is read at run time (TERTIUS_X_CATCHUP), so one build serves every shape.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd); V=$(cd "$N/../t0-3vwx-dynamic-scale-films" && pwd)
B=$(cd "$N/../t0-gbgj-combined-check/tools" && pwd)
X5=e232080fd2c29b94bd639d094cabe336bf7f6291
for name in ${@:-xt xg}; do
  case $name in
    xt) args="$X5 ${X5}:tools/renderer-survey/probe.patch $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch $N/scratch-ownray-log.patch $N/scratch-history-dump.patch" ;;
    xg) args="$X5 ${X5}:tools/renderer-survey/probe.patch $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch $N/scratch-ownray-log.patch $N/scratch-catch-up-share.patch" ;;
    *) echo "no build $name"; exit 1 ;;
  esac
  log=/private/tmp/t0-gbgj/logs/build-$name.log
  echo "$(date +%T) start $name, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB" | tee "$log"
  MIN_GIB=22 bash "$B/build.sh" $name $args >> "$log" 2>&1; code=$?
  echo "$(date +%T) end $name exit $code, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB" | tee -a "$log"
  [ $code = 0 ] || exit $code
done
