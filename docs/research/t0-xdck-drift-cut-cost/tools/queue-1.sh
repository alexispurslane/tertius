#!/usr/bin/env bash
# Tool code of t0-xdck. Queue 1, the image takes under today's jitter (vars.sh). Names t-<variant>-<take>. First the
# control: D4 is t0-ion0's Q3 with this ticket's new switch at its default, so its film must repeat t0-ion0's Q3 film
# (+0.47 / +0.23). Takes: rb the film rect-bay with the own-ray count (not on M, whose main build has no count), lag the
# lag film bay-sun, s the bay still bay-sunbeam at the automatic exposure (with the reference A, for the daub).
T=$(dirname "$0")/take.sh; . $(dirname "$0")/vars.sh
$T t-D4-rb motion rect-bay $D4 TERTIUS_X_OWN_COUNT=own
PROBE=$OUT_M $T t-M-rb motion rect-bay
for v in X D8 D4s C4; do $T t-$v-rb motion rect-bay ${!v} TERTIUS_X_OWN_COUNT=own; done
PROBE=$OUT_M $T t-M-lag lag bay-sun
for v in X D4 D8 D4s C4; do $T t-$v-lag lag bay-sun ${!v}; done
for v in A X D4 D8 D4s; do $T t-$v-s still bay-sunbeam ${!v}; done
PROBE=$OUT_M $T t-M-s still bay-sunbeam
