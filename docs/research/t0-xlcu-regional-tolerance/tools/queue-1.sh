#!/usr/bin/env bash
# Queue 1, step 1: the count without action, on the build with T3. Region, window 18 with the corrected window and the fix's action, with
# TERTIUS_X_REGION_ACT=0 (the test computes and does not shorten) and the count on. The rule does not matter without
# action, because the count holds the lag, T1 and T2 at once. Names: k<jitter>-<case>.
# Stills at the automatic exposure, run draws 100 to 300, every 7th (29 draws; 7 has no common factor with 18). Cabin
# switch: only the switch run, film frames 140 to 419 (run draws 173 to 452), every draw. Film rect-bay: every draw.
T=$(dirname "$0")/take.sh
J=TERTIUS_DEBUG_JITTER=none
K="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_REGION_ACT=0 TERTIUS_X_REGION_COUNT=count"
S="TERTIUS_X_REGION_COUNT_FROM=100 TERTIUS_X_REGION_COUNT_TO=300 TERTIUS_X_REGION_COUNT_EVERY=7"
for c in rect-lamps bay-sunbeam exit-cabin; do
  $T kt-$c still $c $K $S
  $T kn-$c still $c $J $K $S
done
TERTIUS_SURVEY_SWITCH_RUNS=switch $T kt-cabin switch cabin $K TERTIUS_X_REGION_COUNT_FROM=173 TERTIUS_X_REGION_COUNT_TO=452
$T kt-rect-bay motion rect-bay $K TERTIUS_X_REGION_COUNT_FROM=0 TERTIUS_X_REGION_COUNT_TO=100000
