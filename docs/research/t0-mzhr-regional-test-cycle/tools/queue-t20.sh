#!/usr/bin/env bash
# One more tolerance, if time is left: region, window 18, tolerance 20 %, under today's jitter.
T=/private/tmp/t0-mzhr/take.sh
R="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=20 TERTIUS_X_REGION_WINDOW=18"
$T x t-r18t20-e still rect-lamps $R TERTIUS_PROBE_EXPOSURE=4.08
$T x t-r18t20-s still rect-lamps $R
$T x t-r18t20-sw switch cabin $R
$T x t-r18t20-rb motion rect-bay $R
