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
#   xb: the cap: as xg without the share, with the acceptance record's scratch-light-evidence-cap.patch (light evidence
#       restarts a history at 8 draws, so today's plan traces no catch-up ray after a light change). Read by its path.
#   xc: the share with the cap: xg with scratch-light-evidence-cap.patch after the share.
#   xfd, xfn: the take of t0-qvm1, on the final code 05a27262d (Design X with the cap) with t0-3vwx's fitted probe patch
#       and its region, dynamic-scale and history logs, and this record's own-ray log and history dump. xfn adds
#       scratch-no-region-evidence.patch: Design X without region evidence.
#   xts, xgs, xbs, xcs: xt, xg, xb and xc with scratch-first-filmed.patch last, for start.json (macrun.sh's start event):
#       with TERTIUS_X_FIRST_FILMED=1 the first drawn frame draws once, so the start's every draw is a film frame.
#   xgz, xcz: xg and xc with scratch-first-filmed.patch and scratch-frozen.patch last, for the fair test's frozen films
#       (fairrun.sh): with TERTIUS_X_FROZEN=1 a capture right after a capture draws once and keeps every history.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd); V=$(cd "$N/../t0-3vwx-dynamic-scale-films" && pwd)
B=$(cd "$N/../t0-gbgj-combined-check/tools" && pwd)
X5=e232080fd2c29b94bd639d094cabe336bf7f6291
XF=05a27262d9c336ffcc2aed64f7e877565af1bb84
CAP=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/scratch-light-evidence-cap.patch
for name in ${@:-xt xg}; do
  first=; [[ $name == x?s ]] && first=$N/scratch-first-filmed.patch
  [[ $name == x?z ]] && first="$N/scratch-first-filmed.patch $N/scratch-frozen.patch"
  case ${name%[sz]} in
    xt) args="$X5 ${X5}:tools/renderer-survey/probe.patch $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch $N/scratch-ownray-log.patch $N/scratch-history-dump.patch" ;;
    xg) args="$X5 ${X5}:tools/renderer-survey/probe.patch $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch $N/scratch-ownray-log.patch $N/scratch-catch-up-share.patch" ;;
    xfd) args="$XF $V/probe-05a27262d.patch $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch $N/scratch-ownray-log.patch $N/scratch-history-dump.patch" ;;
    xfn) args="$XF $V/probe-05a27262d.patch $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch $N/scratch-ownray-log.patch $N/scratch-history-dump.patch $N/scratch-no-region-evidence.patch" ;;
    xb) args="$X5 ${X5}:tools/renderer-survey/probe.patch $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch $N/scratch-ownray-log.patch $CAP" ;;
    xc) args="$X5 ${X5}:tools/renderer-survey/probe.patch $V/scratch-region-log.patch $V/scratch-dynamic-scale.patch $V/scratch-history-log.patch $N/scratch-ownray-log.patch $N/scratch-catch-up-share.patch $CAP" ;;
    *) echo "no build $name"; exit 1 ;;
  esac
  args="$args $first"
  log=/private/tmp/t0-gbgj/logs/build-$name.log
  echo "$(date +%T) start $name, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB" | tee "$log"
  MIN_GIB=22 bash "$B/build.sh" $name $args >> "$log" 2>&1; code=$?
  echo "$(date +%T) end $name exit $code, disk $(df -Pk / | awk 'NR == 2 {printf "%.1f", $4 / 1048576}') GiB" | tee -a "$log"
  [ $code = 0 ] || exit $code
done
