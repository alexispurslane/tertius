#!/usr/bin/env bash
# Tool code of t0-9fo0. Queue 1, take 1: the still rect-lamps under today's jitter, M and X0 first (the first result to
# send), then the reference A on both builds, then the same at the fixed exposure 4.08 stops for the light measure.
D=$(dirname "$0"); T=$D/take.sh
source $D/vars.sh
PROBE=$OUT_M bash $T s-M still rect-lamps
bash $T s-X0 still rect-lamps
bash $T s-A still rect-lamps $A
PROBE=$OUT_M bash $T s-AM still rect-lamps $A
PROBE=$OUT_M bash $T e-M still rect-lamps $E
bash $T e-X0 still rect-lamps $E
bash $T e-A still rect-lamps $A $E
PROBE=$OUT_M bash $T e-AM still rect-lamps $A $E
