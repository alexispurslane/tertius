#!/usr/bin/env bash
# Tool code of t0-3vwx. builds.sh [name...]: the probe builds of this take, one after another, into
# /private/tmp/t0-gbgj/bins/<name>/probe-bin, through t0-gbgj's build.sh (git archive of the revision or tree, the patches in
# order, the Release demo without the analyzers, the source tree deleted after). The line orchestrator's disk limits of
# 2026-11-22: a build starts at 22 GiB free or more, and a sampler stops it under 20 GiB. The log of each build goes to
# /private/tmp/t0-gbgj/logs/build-<name>.log with the free disk before, the lowest sample and after.
#   x5h: X5, the tree e232080f (the line's merge of main ac5f23915), its probe patch, the region log, the dynamic scale and
#        the history log;
#   m5h: main ac5f23915, its probe patch, the dynamic scale and the history log;
#   x3h: the combined check's merge bcca31e95, its probe patch, the region log, the dynamic scale and the history log;
#   m3h: main a819b10b2, its probe patch, the dynamic scale and the history log.
#   xf, mf: the final take's Design X commit $XF and main commit $MF (final.sh), as x5h and m5h. Where this record holds
#       probe-<first 9 digits of XF>.patch, a probe patch fitted to that commit, xf takes it in place of the commit's own.
# With WAIT_FOR=<log> it first waits until that log shows the line "cost cost-xc-s2" followed by its exit line (the end
# of the acceptance lead's timing session).
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd)
B=$(cd "$T/../../t0-gbgj-combined-check/tools" && pwd)
X5=e232080fd2c29b94bd639d094cabe336bf7f6291
L=/private/tmp/t0-gbgj/logs
free() { df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}'; }
if [ -n "${WAIT_FOR:-}" ]; then
  until grep -A1 'cost cost-xc-s2' "$WAIT_FOR" 2>/dev/null | grep -q ' exit '; do sleep 30; done
  echo "$(date +%T) the timing session ended: $(grep -A1 'cost cost-xc-s2' "$WAIT_FOR" | tail -1 | cut -c1-80)"
fi
for name in "${@:-x5h m5h x3h m3h}"; do for name in $name; do
  case $name in
    x5h) args="$X5 $X5:tools/renderer-survey/probe.patch $N/scratch-region-log.patch $N/scratch-dynamic-scale.patch $N/scratch-history-log.patch" ;;
    m5h) args="ac5f23915 ac5f23915:tools/renderer-survey/probe.patch $N/scratch-dynamic-scale.patch $N/scratch-history-log.patch" ;;
    x3h) args="bcca31e95 bcca31e95:tools/renderer-survey/probe.patch $N/scratch-region-log.patch $N/scratch-dynamic-scale.patch $N/scratch-history-log.patch" ;;
    m3h) args="a819b10b2 a819b10b2:tools/renderer-survey/probe.patch $N/scratch-dynamic-scale.patch $N/scratch-history-log.patch" ;;
    xf) probe=$XF:tools/renderer-survey/probe.patch; [ -f $N/probe-${XF:0:9}.patch ] && probe=$N/probe-${XF:0:9}.patch
        args="$XF $probe $N/scratch-region-log.patch $N/scratch-dynamic-scale.patch $N/scratch-history-log.patch" ;;
    mf) args="$MF $MF:tools/renderer-survey/probe.patch $N/scratch-dynamic-scale.patch $N/scratch-history-log.patch" ;;
    *) echo "no build $name"; exit 1 ;;
  esac
  log=$L/build-$name.log
  echo "before $(free) $(date +%T)" > "$log"
  MIN_GIB=22 bash "$B/build.sh" $name $args >> "$log" 2>&1 & pid=$!
  low=$(free)
  while kill -0 $pid 2>/dev/null; do
    f=$(free); low=$(awk -v a="$f" -v b="$low" 'BEGIN { print (a < b) ? a : b }')
    if awk -v a="$f" 'BEGIN { exit !(a < 20) }'; then echo "under 20 GiB: stopping the build" >> "$log"; pkill -P $pid; kill $pid; fi
    sleep 20
  done
  wait $pid; code=$?
  echo "lowest $low" >> "$log"; echo "after $(free) $(date +%T) exit $code" >> "$log"
  echo "$(date +%T) $name exit $code, disk before $(head -1 "$log" | cut -d' ' -f2), lowest $low, after $(free) GiB"
  [ $code = 0 ] || exit $code
done; done
echo "$(date +%T) builds done"
