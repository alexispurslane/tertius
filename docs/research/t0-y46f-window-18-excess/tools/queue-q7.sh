#!/usr/bin/env bash
# Queue 7. No jitter, lamp hall still at 4.08 stops: the diffuse filter off for A and for the clamps-off cap of 4 draws,
# and caps of 8 and 16 draws with clamps off. Today's jitter (t-): A to D, the corrected window and the clamps-off cap.
# Then the cabin still exit-cabin, A without jitter at three fixed exposures, to choose one without clipping.
T=/private/tmp/t0-y46f/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08; L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
NOF=TERTIUS_DIFFUSE_FILTER=off
$T n-A-nof still rect-lamps $J TERTIUS_A_NO_CLAMP=1 $NOF $E
$T n-cap4-nc-nof still rect-lamps $J TERTIUS_A_NO_CLAMP=1 TERTIUS_X_DIFFUSE_CAP=4 $NOF $E
$T n-cap8-nc still rect-lamps $J TERTIUS_A_NO_CLAMP=1 TERTIUS_X_DIFFUSE_CAP=8 $E
$T n-cap16-nc still rect-lamps $J TERTIUS_A_NO_CLAMP=1 TERTIUS_X_DIFFUSE_CAP=16 $E
R18="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18"
$T t-A still rect-lamps TERTIUS_A_NO_CLAMP=1 $E $L
$T t-B still rect-lamps TERTIUS_A_NO_CLAMP=1 TERTIUS_X_FULL_RATE=1000 $E $L
$T t-C still rect-lamps $R18 $E $L
$T t-D still rect-lamps $R18 TERTIUS_X_FULL_RATE=0 $E $L
$T t-Cw still rect-lamps $R18 TERTIUS_X_WINDOW_ALL=1 $E $L
$T t-cap4-nc still rect-lamps TERTIUS_A_NO_CLAMP=1 TERTIUS_X_DIFFUSE_CAP=4 $E $L
for x in 1 2 3; do $T c-A-x$x still exit-cabin $J TERTIUS_A_NO_CLAMP=1 TERTIUS_PROBE_EXPOSURE=$x; done
