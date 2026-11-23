#!/usr/bin/env bash
# Queue 9 (review fixes), no jitter, lamp hall still at 4.08 stops. In scene light (s-): A, C, the fix, the corrected
# window, A with cap 4, and A and A with cap 4 with the diffuse filter off. Then the sweep G and E with the corrected
# window, through the survey.
S=/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59/docs/research/t0-y46f-window-18-excess/tools/scenetake.sh
T=/private/tmp/t0-y46f/take.sh
J=TERTIUS_DEBUG_JITTER=none; E=TERTIUS_PROBE_EXPOSURE=4.08; L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
REF="$J TERTIUS_A_NO_CLAMP=1"; CUT="$J TERTIUS_X_LIGHT_ACTION=cut"; NOF=TERTIUS_DIFFUSE_FILTER=off
REG="TERTIUS_X_REGION=8 TERTIUS_X_REGION_WINDOW=18"
R18="$CUT $REG TERTIUS_X_REGION_TOL=10"
$S s-A $REF $E
$S s-C $R18 $E
$S s-fix $CUT $E
$S s-Cw $R18 TERTIUS_X_WINDOW_ALL=1 $E
$S s-cap4-nc $REF TERTIUS_X_DIFFUSE_CAP=4 $E
$S s-A-nof $REF $NOF $E
$S s-cap4-nc-nof $REF TERTIUS_X_DIFFUSE_CAP=4 $NOF $E
for t in 20 40 80; do $T n-Dw-t$t still rect-lamps $CUT $REG TERTIUS_X_REGION_TOL=$t TERTIUS_X_FULL_RATE=0 TERTIUS_X_WINDOW_ALL=1 $E $L; done
$T n-Ew still rect-lamps $R18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_FULL_RATE=1000 $E $L
