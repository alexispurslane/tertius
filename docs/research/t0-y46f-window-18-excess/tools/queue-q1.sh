#!/usr/bin/env bash
# Queue 1, no jitter: the reference A and the control run C, then C with the count of history lengths.
T=/private/tmp/t0-y46f/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08
REF="$J TERTIUS_A_NO_CLAMP=1"
R18="$J TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18"
$T n-A still rect-lamps $REF $E
$T n-C still rect-lamps $R18 $E
$T n-C-len still rect-lamps $R18 $E TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7
