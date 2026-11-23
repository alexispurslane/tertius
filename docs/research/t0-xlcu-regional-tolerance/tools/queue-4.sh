#!/usr/bin/env bash
# Queue 4, the review's fix (finding F2): cap 4 with the regional test of T0 acting, cabin switch, today's jitter.
# Cap 4 holds every bounce history at 4 draws, so the test acts only on the reflections. This is the state of
# t0-mzhr's broken "region, window 18", whose settle counts were 39 / 25.
T=$(dirname "$0")/take.sh
T0cap4="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1 TERTIUS_X_DIFFUSE_CAP=4"
$T t-T0cap4-sw switch cabin $T0cap4
