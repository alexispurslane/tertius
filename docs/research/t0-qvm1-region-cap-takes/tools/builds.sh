#!/usr/bin/env bash
# Tool code of the region cap's takes (t0-qvm1). builds.sh <cap commit> [name...]: the takes' builds, one at a time,
# through the acceptance record's build.sh (git archive of a commit, the patches in order, the Release demo into
# /private/tmp/t0-qvm1c/<name>/probe-bin, no build under 22 GiB free). Main's code is 76a4fbfeb, the base of the branch
# t0-qvm1-region-cap: main after it holds only ticket notes.
#   pm: main's code with its survey probe patch, the replay's history dump and region log
#       (../../t0-qvm1-region-repair-replay/scratch-*.patch), and t0-gumm's scratch-first-filmed.patch and
#       scratch-frozen.patch (TERTIUS_X_FROZEN=1: a capture right after a capture draws once and keeps every history).
#   pc: the cap's commit with the same patches, its own probe patch first.
#   tm, tc: the plain Release demos of the two, for the timing.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/../.." && pwd)
B=$N/design-x-acceptance-2026-11-22/tools
P=$N/t0-qvm1-region-repair-replay; G=$N/t0-gumm-catch-up-spread
MAIN=76a4fbfeb; CAP=$1; shift
export BASE=/private/tmp/t0-qvm1c
scratch="$P/scratch-history-dump.patch $P/scratch-region-log.patch $G/scratch-first-filmed.patch $G/scratch-frozen.patch"
for name in ${@:-pm pc tm tc}; do
  case $name in
    pm) args="$MAIN $MAIN:tools/renderer-survey/probe.patch $scratch" ;;
    pc) args="$CAP $CAP:tools/renderer-survey/probe.patch $scratch" ;;
    tm) args="$MAIN" ;;
    tc) args="$CAP" ;;
    *) echo "no build $name"; exit 1 ;;
  esac
  [ -x $BASE/$name/probe-bin/Tertius.Demo ] && continue
  echo "$(date +%T) build $name: $(df -h /System/Volumes/Data | awk 'NR == 2 {print $4}') free"
  bash "$B/build.sh" $name $args > "$BASE/build-$name.log" 2>&1 || { echo "build $name failed: $BASE/build-$name.log"; exit 1; }
done
echo "$(date +%T) builds done"
