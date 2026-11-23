#!/usr/bin/env bash
# Queue 4, no jitter, lamp hall still at 4.08 stops, third build: the control runs A and C, the corrected window
# (TERTIUS_X_WINDOW_ALL=1), and the diffuse length cap of 4 draws (TERTIUS_X_DIFFUSE_CAP=4) without the regional test.
T=/private/tmp/t0-y46f/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08; L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
REF="$J TERTIUS_A_NO_CLAMP=1"; CUT="$J TERTIUS_X_LIGHT_ACTION=cut"
R18="$CUT TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18"
$T n-C3 still rect-lamps $R18 $E
$T n-A3 still rect-lamps $REF $E
$T n-Cw still rect-lamps $R18 TERTIUS_X_WINDOW_ALL=1 $E $L
$T n-Dw still rect-lamps $R18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_FULL_RATE=0 $E $L
$T n-cap4 still rect-lamps $CUT TERTIUS_X_DIFFUSE_CAP=4 $E $L
$T n-cap4-nc still rect-lamps $REF TERTIUS_X_DIFFUSE_CAP=4 $E $L
$T n-cap4-nc-bounce still rect-lamps $REF TERTIUS_X_DIFFUSE_CAP=4 $E TERTIUS_PROBE_SIGNAL=2
$T n-cap4-nc-refl still rect-lamps $REF TERTIUS_X_DIFFUSE_CAP=4 $E TERTIUS_PROBE_SIGNAL=3
