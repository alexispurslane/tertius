#!/usr/bin/env bash
# Tool code of t0-9fo0. Queue 2, after queue 1. Take 2: the cabin switch, M and X0. Take 3: the survey's stand part
# (the bounce light alone) and the stand view as a film with every signal (standfilm.sh), M and X0. Take 5 on M: the
# climb (the bounce light alone at world ticks 300 and 306, the survey's form of take bC), and the switch's lamps-on
# control without jitter (only the run "on", TERTIUS_SURVEY_SWITCH_RUNS=on). The census of young light-age records runs
# on X0 with and without jitter, because main's probe has no census switch (TERTIUS_X_CENSUS).
D=$(dirname "$0"); T=$D/take.sh
source $D/vars.sh
while pgrep -f "queue-1.sh" > /dev/null; do sleep 20; done
PROBE=$OUT_M bash $T w-M switch cabin
bash $T w-X0 switch cabin
PROBE=$OUT_M bash $T st-M stand cabin-stand
bash $T st-X0 stand cabin-stand
PROBE=$OUT_M bash $D/standfilm.sh sf-M
bash $D/standfilm.sh sf-X0
PROBE=$OUT_M bash $T c-M climb cabin-climb
PROBE=$OUT_M bash $T j-M switch cabin $NJ TERTIUS_SURVEY_SWITCH_RUNS=on
bash $T n-X0 census-still rect-lamps
bash $T nj-X0 census-still rect-lamps $NJ
