#!/usr/bin/env bash
# Tool code of t0-acxm. queue-bay.sh: the bay takes, after the merge of the line's head with the shadow fix. Both sides of
# each take hold the fix: main M is c4c1f1f2f, at or after dcd738552. Names t-<side>-<take>, sides of vars.sh. Takes: rb
# the film rect-bay (B6 too, the scratch cut to 6 draws), lag the lag film bay-sun, s the stills bay-sunbeam and
# exit-cabin (with the clamps-off reference A, for the daub).
T=$(dirname "$0")/take.sh
for v in M B B1 B6; do $T t-$v-rb motion rect-bay $v; done
for v in M B B1; do $T t-$v-lag lag bay-sun $v; done
for v in A M B B1; do $T t-$v-s still bay-sunbeam,exit-cabin $v; done
