#!/usr/bin/env bash
# Tool code of t0-xwdz. build.sh: the probe build "xl" into /private/tmp/t0-xwdz/xl/probe-bin, through the acceptance
# record's build.sh (git archive of a commit, the patches in order, the Release demo, no build under 22 GiB free):
# the line's head c768e1ed8 with its survey probe patch, t0-gumm's scratch-first-filmed.patch and scratch-frozen.patch
# (TERTIUS_X_FROZEN=1: a capture right after a capture draws once with no step of time and keeps every history), and
# this record's scratch-tail-log.patch (v3), or PATCH=scratch-tail-log-v2.patch for the v2 logs.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); N=$(cd "$T/../.." && pwd); R=$(cd "$T/.." && pwd)
G=$N/t0-gumm-catch-up-spread; HEAD=c768e1ed8
export BASE=/private/tmp/t0-xwdz; mkdir -p $BASE
echo "$(date +%T) build xl: $(df -h /System/Volumes/Data | awk 'NR == 2 {print $4}') free"
bash "$N/design-x-acceptance-2026-11-22/tools/build.sh" xl $HEAD $HEAD:tools/renderer-survey/probe.patch \
  $G/scratch-first-filmed.patch $G/scratch-frozen.patch $R/${PATCH:-scratch-tail-log.patch} > $BASE/build-xl.log 2>&1 \
  || { echo "build xl failed: $BASE/build-xl.log"; exit 1; }
echo "$(date +%T) build done"
