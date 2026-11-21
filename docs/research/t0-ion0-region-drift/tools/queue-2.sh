#!/usr/bin/env bash
# Queue 2, step 2 under today's jitter: each rule acting, against the reference, the fix and the cap of 4 draws (vars.sh).
# Names: t-<variant>-<take>. Takes: e the lamp hall still at 4.08 stops with the count of lengths, rb the film rect-bay,
# sw the cabin switch, s the lamp hall still at the automatic exposure, lag the three lag films. Then the scene light of
# the lamp hall still without jitter at 4.08 stops (sc-<variant>), against the control's c-sA-hn.
T=$(dirname "$0")/take.sh; S=$(dirname "$0")/scenetake.sh; . $(dirname "$0")/vars.sh
for v in A fix cap4 Q0 Q1 Q3; do $T t-$v-e still rect-lamps ${!v} $E $L; done
for v in fix cap4 Q0 Q1 Q3; do $T t-$v-rb motion rect-bay ${!v}; done
for v in A fix cap4 Q0 Q1 Q3; do $T t-$v-sw switch cabin ${!v}; done
for v in A fix cap4 Q0 Q1 Q3; do $T t-$v-s still rect-lamps ${!v}; done
for v in fix cap4 Q0 Q1 Q3; do $T t-$v-lag lag bay-sun,lamp-hall-16-moving,lamp-hall-64-spot-moving ${!v}; done
for v in fix cap4 Q0 Q1 Q3; do $S sc-$v TERTIUS_DEBUG_JITTER=none ${!v} $E; done
