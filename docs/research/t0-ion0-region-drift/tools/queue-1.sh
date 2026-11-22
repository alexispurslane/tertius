#!/usr/bin/env bash
# Queue 1, step 1: the count without action. The fix with the nearest-half store, the 18-draw window ring (no regional
# lag test) and the drift test with TERTIUS_X_DRIFT_ACT=0 and the count on. Names: k<jitter t|n|l>-<case>.
# Stills: run draws 100 to 300, every 7th (29 draws). rect-lamps at 4.08 stops, so that its images can be compared with
# step 2's fix; the others at the automatic exposure. Cabin switch: only the switch run, film frames 140 to 419 (run
# draws 173 to 452), every draw. Film rect-bay: every draw.
T=$(dirname "$0")/take.sh
K="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_HALF_NEAREST=1 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_DRIFT=8 TERTIUS_X_DRIFT_ACT=0 TERTIUS_X_DRIFT_COUNT=drift"
S="TERTIUS_X_REGION_COUNT_FROM=100 TERTIUS_X_REGION_COUNT_TO=300 TERTIUS_X_REGION_COUNT_EVERY=7"
for j in t n l; do
  case $j in t) J=;; n) J=TERTIUS_DEBUG_JITTER=none;; l) J=TERTIUS_X_LONG_JITTER=1;; esac
  $T k$j-rect-lamps still rect-lamps $J $K $S TERTIUS_PROBE_EXPOSURE=4.08
  $T k$j-bay-sunbeam still bay-sunbeam $J $K $S
  $T k$j-exit-cabin still exit-cabin $J $K $S
done
TERTIUS_SURVEY_SWITCH_RUNS=switch $T kt-cabin switch cabin $K TERTIUS_X_REGION_COUNT_FROM=173 TERTIUS_X_REGION_COUNT_TO=452
$T kt-rect-bay motion rect-bay $K TERTIUS_X_REGION_COUNT_FROM=0 TERTIUS_X_REGION_COUNT_TO=100000
