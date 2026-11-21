#!/usr/bin/env bash
# Queue 1c, the controls of the second build (one drift test per block at its center pixel, and Q2's switch): T0 with
# the old store must repeat the first build's images byte for byte, and the lamp hall count of queue 1 must repeat its
# pixel shares, with the shader's block flags equal to driftcount.py's center rule.
T=$(dirname "$0")/take.sh
T0="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1"
K="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_HALF_NEAREST=1 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_DRIFT=8 TERTIUS_X_DRIFT_ACT=0 TERTIUS_X_DRIFT_COUNT=drift"
S="TERTIUS_X_REGION_COUNT_FROM=100 TERTIUS_X_REGION_COUNT_TO=300 TERTIUS_X_REGION_COUNT_EVERY=7"
$T c2-T0-e still rect-lamps $T0 TERTIUS_PROBE_EXPOSURE=4.08
$T kt2-rect-lamps still rect-lamps $K $S TERTIUS_PROBE_EXPOSURE=4.08
