#!/usr/bin/env bash
# Tool code of t1-g1jd. builds.sh [name...]: the four probe builds of the count of short diffuse histories by writer, each
# from main ae4364022 with the survey's probe patch and ../scratch-history-dump.patch, through the acceptance record's
# build.sh (git archive, the patches in order with GNU patch, the Release demo into /private/tmp/t0-5tyj/<name>/probe-bin,
# no build under 22 GiB free). One build at a time.
#   g1-main: main as it is.
#   g1-noreg: region evidence off (../scratch-no-region-evidence.patch: the drift test runs, its evidence caps nothing).
#   g1-noal: the anti-lag after the clamp skipped for the diffuse signal only (../scratch-no-diffuse-antilag.patch).
#   g1-none: both off.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/.." && pwd)
B=$(cd "$N/../design-x-acceptance-2026-11-22/tools" && pwd)
MAIN=ae4364022
base="$MAIN:tools/renderer-survey/probe.patch $N/scratch-history-dump.patch"
for name in ${@:-g1-main g1-noreg g1-noal g1-none}; do
  case $name in
    g1-main) extra= ;;
    g1-noreg) extra=$N/scratch-no-region-evidence.patch ;;
    g1-noal) extra=$N/scratch-no-diffuse-antilag.patch ;;
    g1-none) extra="$N/scratch-no-region-evidence.patch $N/scratch-no-diffuse-antilag.patch" ;;
    *) echo "no build $name"; exit 1 ;;
  esac
  [ -x /private/tmp/t0-5tyj/$name/probe-bin/Tertius.Demo ] && continue
  echo "$(date +%T) build $name: $(df -h /System/Volumes/Data | awk 'NR == 2 {print $4}') free"
  bash "$B/build.sh" $name $MAIN $base $extra || exit 1
done
