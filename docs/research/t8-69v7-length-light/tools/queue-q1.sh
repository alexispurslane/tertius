#!/usr/bin/env bash
# Queue 1: the frame (no jitter, clamps off, 4.08 stops, scene light) with the stage means (ST), loop closed and open,
# filter on and off, caps of 32 (no cap) and 4 draws. a-plain has no stage readback, as its control.
S=/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59/docs/research/t8-69v7-length-light/tools/scenetake.sh
F="TERTIUS_DEBUG_JITTER=none TERTIUS_A_NO_CLAMP=1 TERTIUS_PROBE_EXPOSURE=4.08"
ST="TERTIUS_X_STAGES=stages TERTIUS_X_LENGTHS_EVERY=7"
C4=TERTIUS_X_DIFFUSE_CAP=4; NOF=TERTIUS_DIFFUSE_FILTER=off; SB=TERTIUS_X_SINGLE_BOUNCE=1
$S a-plain $F
$S a $F $ST
$S c4 $F $C4 $ST
$S a-nof $F $NOF $ST
$S c4-nof $F $C4 $NOF $ST
$S a-sb $F $SB $ST
$S c4-sb $F $C4 $SB $ST
$S a-nof-sb $F $NOF $SB $ST
$S c4-nof-sb $F $C4 $NOF $SB $ST
