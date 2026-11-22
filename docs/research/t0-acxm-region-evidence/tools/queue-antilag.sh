#!/usr/bin/env bash
# Tool code of t0-acxm. queue-antilag.sh: the takes of the anti-lag rule (sides L, R and S of vars.sh), after the
# settlement of 2026-11-22: the film rect-bay, the lag film bay-sun, and the stills bay-sunbeam and exit-cabin.
T=$(dirname "$0")/take.sh
for v in L R S; do $T t-$v-rb motion rect-bay $v; done
for v in L R S; do $T t-$v-lag lag bay-sun $v; done
for v in L R; do $T t-$v-s still bay-sunbeam,exit-cabin $v; done
