#!/usr/bin/env bash
# Queue c, the control runs: the lamp hall still at 4.08 stops, the reference A and rule T0 with action (region, window
# 18, the corrected window, tolerance 10 %), under today's jitter (t-) and without jitter (n-). T0 counts the history
# lengths, as t0-y46f did. Then T0 through the new path of the derived rules (rule 0 with the count on), which must
# repeat T0's images.
T=$(dirname "$0")/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08; L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
REF=TERTIUS_A_NO_CLAMP=1
T0="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1"
$T t-A-e still rect-lamps $REF $E
$T t-T0-e still rect-lamps $T0 $E $L
$T n-A-e still rect-lamps $J $REF $E
$T n-T0-e still rect-lamps $J $T0 $E $L
$T t-T0path-e still rect-lamps $T0 $E TERTIUS_X_REGION_COUNT=count
