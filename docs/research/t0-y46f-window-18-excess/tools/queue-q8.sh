#!/usr/bin/env bash
# Queue 8, no jitter, cabin still exit-cabin at 1.0 stops: A to D, the corrected window and the clamps-off cap of 4 draws.
T=/private/tmp/t0-y46f/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=1.0; L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
REF="$J TERTIUS_A_NO_CLAMP=1"
R18="$J TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18"
$T c-A still exit-cabin $REF $E $L
$T c-B still exit-cabin $REF TERTIUS_X_FULL_RATE=1000 $E $L
$T c-C still exit-cabin $R18 $E $L
$T c-D still exit-cabin $R18 TERTIUS_X_FULL_RATE=0 $E $L
$T c-Cw still exit-cabin $R18 TERTIUS_X_WINDOW_ALL=1 $E $L
$T c-cap4-nc still exit-cabin $REF TERTIUS_X_DIFFUSE_CAP=4 $E $L
