#!/usr/bin/env bash
# Queue 6, no jitter, lamp hall still at 4.08 stops: long.py's control (28 captures, as the survey), then long stills
# of 180 captures (draws to 5,908) of A, the diffuse cap of 4 draws with clamps off, and C.
T=/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59/docs/research/t0-y46f-window-18-excess/tools/longtake.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08
REF="$J TERTIUS_A_NO_CLAMP=1"
R18="$J TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18"
LL="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7 TERTIUS_X_LENGTHS_FROM=5281"
$T n-A-l28 28 $REF $E
$T n-A-long 180 $REF $E $LL
$T n-cap4-nc-long 180 $REF TERTIUS_X_DIFFUSE_CAP=4 $E $LL
$T n-C-long 180 $R18 $E $LL
