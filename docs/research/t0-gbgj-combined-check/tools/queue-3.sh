#!/usr/bin/env bash
# Tool code of t0-gbgj. queue-3.sh: the take that the line orchestrator added for the acceptance lead's finding of bright
# jambs: the switch "cabin" with pictures, at a constant render scale of 0.956 and at 2/3, on M2 and X2. It builds m2s and
# x2s (M2's and X2's probe builds with scratch-render-scale.patch), then runs scalefilm.sh: the 0.956 runs on m2s and x2s
# with TERTIUS_X_RENDER_SCALE=0.956, and the 2/3 runs on the same builds without it, which is the headless 2/3 of the
# engine as it is.
T=$(cd "$(dirname "$0")" && pwd); . "$T/vars.sh"; D=$(cd "$T/.." && pwd)
[ -x "$W/bins/m2s/probe-bin/Tertius.Demo" ] || bash "$T/build.sh" m2s "$MAIN_REV" "$D/scratch-probe-m2.patch" "$D/scratch-render-scale.patch"
[ -x "$W/bins/x2s/probe-bin/Tertius.Demo" ] || bash "$T/build.sh" x2s a7e373197 a7e373197:tools/renderer-survey/probe.patch "$D/scratch-render-scale.patch"
bash "$T/scalefilm.sh" sc-M2-956 m2s 0.956
bash "$T/scalefilm.sh" sc-X2-956 x2s 0.956
bash "$T/scalefilm.sh" sc-M2-23 m2s
bash "$T/scalefilm.sh" sc-X2-23 x2s
echo "$(date +%T) queue-3 done"
