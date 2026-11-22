#!/usr/bin/env bash
# Queue 2, second build: the control runs, the nearest-half store (HN), without FSR (NAT), and each signal alone with
# the filter off (b bounce, r reflections).
S=/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59/docs/research/t8-69v7-length-light/tools/scenetake.sh
F="TERTIUS_DEBUG_JITTER=none TERTIUS_A_NO_CLAMP=1 TERTIUS_PROBE_EXPOSURE=4.08"
ST="TERTIUS_X_STAGES=stages TERTIUS_X_LENGTHS_EVERY=7"
C4=TERTIUS_X_DIFFUSE_CAP=4; NOF=TERTIUS_DIFFUSE_FILTER=off; HN=TERTIUS_X_HALF_NEAREST=1; NAT=SCENE_NATIVE=1
$S a2 $F $ST
$S c4-2 $F $C4 $ST
$S a-hn $F $HN $ST
$S c4-hn $F $C4 $HN $ST
$S a-nof-hn $F $NOF $HN $ST
$S c4-nof-hn $F $C4 $NOF $HN $ST
$S a-nof-nat $F $NOF $NAT $ST
$S c4-nof-nat $F $C4 $NOF $NAT $ST
$S a-nat $F $NAT $ST
$S c4-nat $F $C4 $NAT $ST
$S a-nof-b $F $NOF TERTIUS_PROBE_SIGNAL=2
$S c4-nof-b $F $C4 $NOF TERTIUS_PROBE_SIGNAL=2
$S a-nof-r $F $NOF TERTIUS_PROBE_SIGNAL=3
$S c4-nof-r $F $C4 $NOF TERTIUS_PROBE_SIGNAL=3
