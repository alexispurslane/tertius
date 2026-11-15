#!/usr/bin/env bash
# Queue 4, on the fourth build: Q2 acting through queue 2's takes under today's jitter, then the film rect-bay with its
# levels at frames 10, 20 and 30 (filmearly.py, names t-<variant>-fe) for the fix, the cap of 4 draws and each rule.
T=$(dirname "$0")/take.sh; S=$(dirname "$0")/scenetake.sh; F=$(dirname "$0")/earlytake.sh; . $(dirname "$0")/vars.sh
$T t-Q2-e still rect-lamps $Q2 $E $L
$T t-Q2-rb motion rect-bay $Q2
$T t-Q2-sw switch cabin $Q2
$T t-Q2-s still rect-lamps $Q2
$T t-Q2-lag lag bay-sun,lamp-hall-16-moving,lamp-hall-64-spot-moving $Q2
$S sc-Q2 TERTIUS_DEBUG_JITTER=none $Q2 $E
for v in fix cap4 Q0 Q1 Q3 Q2; do $F t-$v-fe ${!v}; done
