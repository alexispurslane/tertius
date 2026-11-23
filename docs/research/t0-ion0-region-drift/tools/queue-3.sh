#!/usr/bin/env bash
# Queue 3, step 2 without jitter (prefix n-) and under the long Halton sequence (prefix l-), on the fourth build: the
# reference, the fix and the rules Q1, Q3 and Q2. Q0 and the cap of 4 draws are left out: under today's jitter both
# swing the cabin's center before the switch (102 to 124 % and 91 to 124 % of the reference), so neither keeps the still.
# Takes as in queue 2: e the lamp hall still at 4.08 stops with the count of lengths, sw the cabin switch, s the lamp
# hall still at the automatic exposure.
T=$(dirname "$0")/take.sh; . $(dirname "$0")/vars.sh
# The strips check's question 1: the cap of 4 draws in the cabin switch without jitter, beside queue 2's t-cap4-sw.
$T n-cap4-sw switch cabin TERTIUS_DEBUG_JITTER=none $cap4
for j in n l; do
  case $j in n) J=TERTIUS_DEBUG_JITTER=none;; l) J=TERTIUS_X_LONG_JITTER=1;; esac
  for v in A fix Q1 Q3 Q2; do $T $j-$v-e still rect-lamps $J ${!v} $E $L; done
  for v in A fix Q1 Q3 Q2; do $T $j-$v-sw switch cabin $J ${!v}; done
  for v in A fix Q1 Q3 Q2; do $T $j-$v-s still rect-lamps $J ${!v}; done
done
