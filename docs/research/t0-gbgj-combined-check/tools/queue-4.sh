#!/usr/bin/env bash
# Tool code of t0-gbgj. queue-4.sh <step>: the takes with the fair lamp switch (the probe turns a lamp's glow off with its
# light, TERTIUS_PROBE_LAMP_GLOW=off by default). The builds m2g and x2g are M2's and X2's probe builds with the glow
# change (main's probe-main-2ffd7e66f-glow.patch of 68239c0af, and the line's probe.patch of 68239c0af with its Count
# hunk fitted to 12 to 14) and scratch-render-scale.patch; diskbuild.sh makes them. Steps:
#   m2: the off-draw check and the stays check on M2 (scg-M2-23 with the fair switch at 2/3, then sck-M2-23 with
#       TERTIUS_PROBE_LAMP_GLOW=stays at 2/3, whose frames must equal sc-M2-23's);
#   takes: the jamb frames at 0.956 and 2/3 on both sides (scg-*), then the survey's switch "cabin" at the headless 2/3
#       with its region series (wg-M2, wg-X2), as take 2 ran it.
T=$(cd "$(dirname "$0")" && pwd); . "$T/vars.sh"
case $1 in
  m2)
    bash "$T/scalefilm.sh" scg-M2-23 m2g
    TERTIUS_PROBE_LAMP_GLOW=stays bash "$T/scalefilm.sh" sck-M2-23 m2g ;;
  takes)
    bash "$T/scalefilm.sh" scg-X2-23 x2g
    bash "$T/scalefilm.sh" scg-M2-956 m2g 0.956
    bash "$T/scalefilm.sh" scg-X2-956 x2g 0.956
    bash "$T/take.sh" wg-M2 switch cabin m2g
    bash "$T/take.sh" wg-X2 switch cabin x2g ;;
esac
echo "$(date +%T) queue-4 $1 done"
