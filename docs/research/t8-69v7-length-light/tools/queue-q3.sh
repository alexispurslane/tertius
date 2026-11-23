#!/usr/bin/env bash
# Queue 3: the dose check. Caps of 8 and 16 draws, filter on, plain and with the nearest-half store (HN). Filter off,
# with FSR and without (NAT). And the filter-off cap of 4 without FSR and with HN.
S=/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59/docs/research/t8-69v7-length-light/tools/scenetake.sh
F="TERTIUS_DEBUG_JITTER=none TERTIUS_A_NO_CLAMP=1 TERTIUS_PROBE_EXPOSURE=4.08"
ST="TERTIUS_X_STAGES=stages TERTIUS_X_LENGTHS_EVERY=7"
NOF=TERTIUS_DIFFUSE_FILTER=off; HN=TERTIUS_X_HALF_NEAREST=1; NAT=SCENE_NATIVE=1
for c in 8 16; do $S c$c $F TERTIUS_X_DIFFUSE_CAP=$c $ST; $S c$c-hn $F TERTIUS_X_DIFFUSE_CAP=$c $HN $ST; done
for c in 8 16; do $S c$c-nof $F TERTIUS_X_DIFFUSE_CAP=$c $NOF $ST; $S c$c-nof-nat $F TERTIUS_X_DIFFUSE_CAP=$c $NOF $NAT $ST; done
$S a-nof-nat-hn $F $NOF $NAT $HN $ST
$S c4-nof-nat-hn $F TERTIUS_X_DIFFUSE_CAP=4 $NOF $NAT $HN $ST
touch /private/tmp/t8-69v7/q3.done
