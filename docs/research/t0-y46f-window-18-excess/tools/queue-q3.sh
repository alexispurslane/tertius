#!/usr/bin/env bash
# Queue 3, no jitter, lamp hall still at 4.08 stops: A and C per signal (TERTIUS_PROBE_SIGNAL 2 bounce, 3 reflections),
# C with both clamps off, and the tolerance sweep G of D.
T=/private/tmp/t0-y46f/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08; L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
REF="$J TERTIUS_A_NO_CLAMP=1"; CUT="$J TERTIUS_X_LIGHT_ACTION=cut"
REG="TERTIUS_X_REGION=8 TERTIUS_X_REGION_WINDOW=18"
R18="$CUT $REG TERTIUS_X_REGION_TOL=10"
$T n-A-bounce still rect-lamps $REF $E TERTIUS_PROBE_SIGNAL=2
$T n-C-bounce still rect-lamps $R18 $E TERTIUS_PROBE_SIGNAL=2
$T n-A-refl still rect-lamps $REF $E TERTIUS_PROBE_SIGNAL=3
$T n-C-refl still rect-lamps $R18 $E TERTIUS_PROBE_SIGNAL=3
$T n-C-noclamp still rect-lamps $REF TERTIUS_X_LIGHT_ACTION=cut $REG TERTIUS_X_REGION_TOL=10 $E $L
for t in 20 40 80; do $T n-D-t$t still rect-lamps $CUT $REG TERTIUS_X_REGION_TOL=$t TERTIUS_X_FULL_RATE=0 $E $L; done
