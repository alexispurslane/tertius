#!/usr/bin/env bash
# Queue 1m, step 1 again on the third build, which adds Q2 (vars.sh) and three count layers. First the controls: Q1
# acting must repeat queue 2's lamp hall still of the second build byte for byte, and the count's first five layers must
# repeat queue 1's. Then queue 1's takes with the prefix m (m<jitter>-<case>), so that one count holds Q0, Q1, Q3 and Q2.
T=$(dirname "$0")/take.sh; . $(dirname "$0")/vars.sh
$T y-Q1-e still rect-lamps $Q1 $E $L
K="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_HALF_NEAREST=1 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_DRIFT=8 TERTIUS_X_DRIFT_ACT=0 TERTIUS_X_DRIFT_COUNT=drift"
S="TERTIUS_X_REGION_COUNT_FROM=100 TERTIUS_X_REGION_COUNT_TO=300 TERTIUS_X_REGION_COUNT_EVERY=7"
for j in t n l; do
  case $j in t) J=;; n) J=TERTIUS_DEBUG_JITTER=none;; l) J=TERTIUS_X_LONG_JITTER=1;; esac
  $T m$j-rect-lamps still rect-lamps $J $K $S $E
  $T m$j-bay-sunbeam still bay-sunbeam $J $K $S
  $T m$j-exit-cabin still exit-cabin $J $K $S
done
TERTIUS_SURVEY_SWITCH_RUNS=switch $T mt-cabin switch cabin $K TERTIUS_X_REGION_COUNT_FROM=173 TERTIUS_X_REGION_COUNT_TO=452
$T mt-rect-bay motion rect-bay $K TERTIUS_X_REGION_COUNT_FROM=0 TERTIUS_X_REGION_COUNT_TO=100000
