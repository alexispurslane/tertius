#!/usr/bin/env bash
# Tool code of t0-y88b. builds.sh <fix commit> [name...]: the takes' probe builds, one at a time, through the acceptance
# record's build.sh (git archive of a commit, the patches in order, the Release demo into /private/tmp/t0-y88b/<name>/probe-bin,
# no build under 22 GiB free):
#   ym: main 496e3affc, the base of the branch t0-y88b-still-place;
#   yf: the fix's commit.
# Each takes its tree's survey probe patch, t0-gumm's scratch-first-filmed.patch and scratch-frozen.patch (frozen films),
# t0-xwdz's scratch-tail-log.patch (the tail log of the reflections' lengths and own rays) and this record's
# scratch-yaw-drift.patch (TERTIUS_X_YAW_TEXELS: a slow pan).
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/.." && pwd); N=$(cd "$T/../.." && pwd)
MAIN=496e3affc; FIX=$1; shift
export BASE=/private/tmp/t0-y88b; mkdir -p $BASE
G=$N/t0-gumm-catch-up-spread
scratch="$G/scratch-first-filmed.patch $G/scratch-frozen.patch $N/t0-xwdz-reflection-tail/scratch-tail-log.patch $R/scratch-yaw-drift.patch"
for name in ${@:-ym yf}; do
  case $name in
    ym) commit=$MAIN ;;
    yf) commit=$FIX ;;
    *) echo "no build $name"; exit 1 ;;
  esac
  echo "$(date +%T) build $name ($commit): $(df -h /System/Volumes/Data | awk 'NR == 2 {print $4}') free"
  bash "$N/design-x-acceptance-2026-11-22/tools/build.sh" $name $commit $commit:tools/renderer-survey/probe.patch $scratch \
    > "$BASE/build-$name.log" 2>&1 || { echo "build $name failed: $BASE/build-$name.log"; exit 1; }
done
echo "$(date +%T) builds done"
