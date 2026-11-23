#!/usr/bin/env bash
# Tool code of t0-gbgj. queue-5.sh: the stills sample at a constant render scale of 0.956 that the line orchestrator moved
# here: the survey's still part of rect-lamps, exit-cabin, deck-cuts and bay-sunbeam at the automatic exposure, and of
# rect-lamps at 4.08 stops, on m2g and x2g (M2's and X2's probe builds with scratch-render-scale.patch) with
# TERTIUS_X_RENDER_SCALE=0.956. The 2/3 stills beside them are take 1's runs s-* and e-*.
T=$(cd "$(dirname "$0")" && pwd)
for side in M2 X2; do b=$(echo $side | tr A-Z a-z)g
  bash "$T/take.sh" s9-$side still rect-lamps,exit-cabin,deck-cuts,bay-sunbeam $b TERTIUS_X_RENDER_SCALE=0.956
  bash "$T/take.sh" e9-$side still rect-lamps $b TERTIUS_X_RENDER_SCALE=0.956 TERTIUS_PROBE_EXPOSURE=4.08
done
echo "$(date +%T) queue-5 done"
