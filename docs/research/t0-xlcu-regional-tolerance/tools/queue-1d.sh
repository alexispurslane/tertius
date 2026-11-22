#!/usr/bin/env bash
# Queue 1d, after queue 1 on the build with T3. A diagnostic of step 1 (not in the ticket): the lamp hall count of
# queue 1 under today's jitter with the diffuse filter off, which tests whether the filter's write-back into the slow
# history causes the still's lag. Then the control runs of queue c again on this build (t-T0-e2, n-T0-e2), which must
# repeat the first build's images byte for byte, because rule 0 without the count keeps the old path.
T=$(dirname "$0")/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08
K="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_REGION_ACT=0 TERTIUS_X_REGION_COUNT=count"
S="TERTIUS_X_REGION_COUNT_FROM=100 TERTIUS_X_REGION_COUNT_TO=300 TERTIUS_X_REGION_COUNT_EVERY=7"
T0="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1"
$T kt-rect-lamps-nof still rect-lamps $K $S TERTIUS_DIFFUSE_FILTER=off
$T t-T0-e2 still rect-lamps $T0 $E
$T n-T0-e2 still rect-lamps $J $T0 $E
