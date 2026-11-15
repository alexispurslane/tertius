#!/usr/bin/env bash
# Queue 3, step 2 under the long Halton sequence (TERTIUS_X_LONG_JITTER=1, prefix l-): the reference, the fix, T0 and
# T1, the best derived rule after queue 2 (README, "Changes to the ticket's design"). Takes as in queue 2. Then the bay-sun
# lag film of cap4 under today's jitter, which the ticket does not ask for.
T=$(dirname "$0")/take.sh
LJ=TERTIUS_X_LONG_JITTER=1; E=TERTIUS_PROBE_EXPOSURE=4.08; L="TERTIUS_X_LENGTHS=lengths TERTIUS_X_LENGTHS_EVERY=7"
A=TERTIUS_A_NO_CLAMP=1
fix=TERTIUS_X_LIGHT_ACTION=cut
T0="$fix TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1"
T1="$T0 TERTIUS_X_REGION_RULE=1"
cap4="$fix TERTIUS_X_DIFFUSE_CAP=4"
for v in A fix T0 T1; do $T l-$v-e still rect-lamps $LJ ${!v} $E $L; done
for v in A fix T0 T1; do $T l-$v-sw switch cabin $LJ ${!v}; done
$T t-cap4-lag lag bay-sun $cap4
