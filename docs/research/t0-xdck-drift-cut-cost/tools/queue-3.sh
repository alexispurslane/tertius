#!/usr/bin/env bash
# Tool code of t0-xdck. Queue 3, after the review, on the build with TERTIUS_X_DRIFT_CENTERS as 0, 1 or 2: the own-ray
# count of Xt's film (the review asked whether Xt traces own rays like X), then two timing rounds of X, Xt, Xtc and Xtn,
# each order then its reverse. Xtn separates the cost of the test's code in the shaders from the cost of running it.
D=$(dirname "$0"); T=$D/take.sh
source $D/vars.sh
bash $T t-Xt-rb motion rect-bay $Xt TERTIUS_X_OWN_COUNT=own
bash $D/round.sh r7 M X Xt Xtc Xtn
bash $D/round.sh r8 Xtn Xtc Xt X M
