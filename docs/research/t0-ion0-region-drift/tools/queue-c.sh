#!/usr/bin/env bash
# Queue c, the control runs. (1) Every new switch at its default and the old store: the lamp hall still at 4.08 stops,
# the reference A and the tolerance record's T0, today's jitter. It must repeat 96.4 / 101.4. (2) The ported
# nearest-half store (HN) in scene light, no jitter, clamps off, as t8-69v7's queue 2: A, A with HN, cap 4 with HN.
# It must repeat 102.7 / 105.8 (A with HN against A) and 100.0 / 100.1 (cap 4 with HN against A with HN).
T=$(dirname "$0")/take.sh; S=$(dirname "$0")/scenetake.sh
E=TERTIUS_PROBE_EXPOSURE=4.08
T0="TERTIUS_X_LIGHT_ACTION=cut TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10 TERTIUS_X_REGION_WINDOW=18 TERTIUS_X_WINDOW_ALL=1"
$T c-A-e still rect-lamps TERTIUS_A_NO_CLAMP=1 $E
$T c-T0-e still rect-lamps $T0 $E
F="TERTIUS_DEBUG_JITTER=none TERTIUS_A_NO_CLAMP=1 $E"
$S c-sA $F
$S c-sA-hn $F TERTIUS_X_HALF_NEAREST=1
$S c-sc4-hn $F TERTIUS_X_DIFFUSE_CAP=4 TERTIUS_X_HALF_NEAREST=1
