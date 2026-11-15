#!/usr/bin/env bash
# The diagnostic takes after the queues, today's jitter: the region test with both history clamps off, to test whether the
# clamp's loss of bright samples drives the test.
T=/private/tmp/t0-mzhr/take.sh
$T x t-r18nc-sw switch cabin TERTIUS_A_NO_CLAMP=1 TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18
$T x t-r4nc-sw switch cabin TERTIUS_A_NO_CLAMP=1 TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10
