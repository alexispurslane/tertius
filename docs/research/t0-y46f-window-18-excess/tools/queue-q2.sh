#!/usr/bin/env bash
# Queue 2, no jitter, lamp hall still at 4.08 stops: the control C on the second build, then every variant with the
# count of history lengths (L). Names n-<variant>.
T=/private/tmp/t0-y46f/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08; L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
REF="$J TERTIUS_A_NO_CLAMP=1"; CUT="$J TERTIUS_X_LIGHT_ACTION=cut"
R18="$CUT TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18"
$T n-C2 still rect-lamps $R18 $E
$T n-A-len still rect-lamps $REF $E $L
$T n-B still rect-lamps $REF TERTIUS_X_FULL_RATE=1000 $E $L
$T n-C-len2 still rect-lamps $R18 $E $L
$T n-D still rect-lamps $R18 TERTIUS_X_FULL_RATE=0 $E $L
$T n-E still rect-lamps $R18 TERTIUS_X_FULL_RATE=1000 $E $L
$T n-fix still rect-lamps $CUT $E $L
$T n-F still rect-lamps $CUT TERTIUS_X_FULL_RATE=1000 $E $L
$T n-Acap still rect-lamps $REF TERTIUS_X_MOST_WEIGHT=1e30 $E $L
