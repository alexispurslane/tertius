#!/usr/bin/env bash
# The controls of the new switches: each run with every new switch at its default must repeat a result of the first session.
T=/private/tmp/t0-mzhr/take.sh
$T x c-r8-sw switch cabin TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10
$T x c-r8-s still rect-lamps TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10
$T x c-r8-e still rect-lamps TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_PROBE_EXPOSURE=4.08
