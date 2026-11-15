#!/usr/bin/env bash
# Queue s, the strips check, on the fourth build. First its control: with TERTIUS_NO_SUN unset, Q1 acting must repeat
# the lamp hall still of queue 2 byte for byte. Then, all with the nearest-half store: the lamp hall still at 4.08 stops
# (s-<jitter>-<variant>-<sun|nosun>-e) and in scene light (ss-...), under today's jitter (t) and none (n), for the
# reference A and "region, window 4" (W4: the fix with the regional lag test, the fast history as its recent mean,
# tolerance 10 %, t0-mzhr's R4). Then the cabin switch under today's jitter without the sun, for the reference and for
# the cap of 4 draws. With the sun, those are queue 2's t-A-sw and t-cap4-sw.
T=$(dirname "$0")/take.sh; S=$(dirname "$0")/scenetake.sh; . $(dirname "$0")/vars.sh
W4="$fix TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10"
NS=TERTIUS_NO_SUN=1
$T z-Q1-e still rect-lamps $Q1 $E $L
for j in t n; do
  J=$([ $j = n ] && echo TERTIUS_DEBUG_JITTER=none)
  for v in A W4; do
    $T s-$j-$v-sun-e still rect-lamps $J ${!v} $E
    $T s-$j-$v-nosun-e still rect-lamps $J ${!v} $NS $E
    $S ss-$j-$v-sun $J ${!v} $E
    $S ss-$j-$v-nosun $J ${!v} $NS $E
  done
done
$T s-t-A-nosun-sw switch cabin $A $NS
$T s-t-cap4-nosun-sw switch cabin $cap4 $NS
