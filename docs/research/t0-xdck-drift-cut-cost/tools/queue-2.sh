#!/usr/bin/env bash
# Tool code of t0-xdck. Queue 2, after the review: the drift test at the blocks' center pixels only (vars.sh, Xtc, D4c,
# D8c). The films of D4c and D8c must repeat D4's and D8's images, because only the centers' test gives evidence. Then
# two timing rounds, each order then its reverse, with M, X and Xt as the links to rounds 1 to 4.
D=$(dirname "$0"); T=$D/take.sh
source $D/vars.sh
for v in D8c D4c; do bash $T t-$v-rb motion rect-bay ${!v} TERTIUS_X_OWN_COUNT=own; done
bash $D/round.sh r5 M X Xt Xtc D4c D8c
bash $D/round.sh r6 D8c D4c Xtc Xt X M
