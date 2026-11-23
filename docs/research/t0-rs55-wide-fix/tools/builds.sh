#!/usr/bin/env bash
# Tool code of t0-rs55. builds.sh <name...>: the probe builds of the wide fix's takes, into /private/tmp/t0-gbgj/bins/<name>/
# probe-bin, through t0-gbgj's build.sh (git archive of a revision, the patches in order, the Release demo without the
# analyzers). A build starts at 22 GiB free or more (the line orchestrator's limit); its log goes to
# /private/tmp/t0-gbgj/logs/build-<name>.log.
#   m0z: today's fix, main 86ae0fbc8, with the survey's probe patch (its own, from that revision), the acceptance record's
#        scratch-dynamic-scale-step2.patch (TERTIUS_X_FRAME_TIME_TARGET, TERTIUS_X_SIZE_REPLAY, TERTIUS_X_SIZE_LOG and
#        TERTIUS_X_TIME_LOG), and t0-gumm's scratch-first-filmed.patch and scratch-frozen.patch (the frozen films of
#        fairrun.sh), and scratch-sun-switch.patch (TERTIUS_X_SUN_OFF). Every patch acts only under its variable.
#   m1z: the wide fix, the commit WIDE (default: the branch t0-rs55-wide-fix's code commit), with the same patches.
#   xrz: the real form on the scratch form's base: t0-gumm's xwz chain (X5 with its probe patch, t0-3vwx's region, dynamic
#        scale and history logs, the own-ray log, the catch-up share, short and delay patches and the cap) with
#        check-real-fix-on-x5.patch in place of scratch-wide-fix.patch, then the first-filmed and frozen patches. Its runs
#        must equal xwz's runs with TERTIUS_X_WIDE_FIX=1 bit for bit.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); W=$(cd "$T/.." && pwd); D=$(cd "$W/.." && pwd)
N=$D/t0-gumm-catch-up-spread; V=$D/t0-3vwx-dynamic-scale-films; A=$D/design-x-acceptance-2026-11-22
B=$D/t0-gbgj-combined-check/tools
X5=e232080fd2c29b94bd639d094cabe336bf7f6291
MAIN=86ae0fbc8
CAP=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/scratch-light-evidence-cap.patch
MEASURE="$A/scratch-dynamic-scale-step2.patch $N/scratch-first-filmed.patch $N/scratch-frozen.patch $N/scratch-sun-switch.patch"
for name in "$@"; do
  case $name in
    m0z) args="$MAIN ${MAIN}:tools/renderer-survey/probe.patch $MEASURE" ;;
    m1z) args="${WIDE:-t0-rs55-wide-fix} ${WIDE:-t0-rs55-wide-fix}:tools/renderer-survey/probe.patch $MEASURE" ;;
    xrz) args="$X5 ${X5}:tools/renderer-survey/probe.patch $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch $N/scratch-ownray-log.patch $N/scratch-catch-up-share.patch $N/scratch-catch-up-short.patch $N/scratch-catch-up-delay.patch $CAP $W/check-real-fix-on-x5.patch $N/scratch-first-filmed.patch $N/scratch-frozen.patch" ;;
    *) echo "no build $name"; exit 1 ;;
  esac
  log=/private/tmp/t0-gbgj/logs/build-$name.log
  echo "$(date +%T) start $name, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB" | tee "$log"
  MIN_GIB=22 bash "$B/build.sh" $name $args >> "$log" 2>&1; code=$?
  echo "$(date +%T) end $name exit $code, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB" | tee -a "$log"
  [ $code = 0 ] || exit $code
done
