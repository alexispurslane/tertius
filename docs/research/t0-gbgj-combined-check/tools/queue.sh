#!/usr/bin/env bash
# Tool code of t0-gbgj. queue.sh: the image takes in order, into /private/tmp/t0-gbgj/runs, on the probe builds of
# builds.sh. Run names: s- stills at the automatic exposure, e- rect-lamps at 4.08 stops, w- the switch cabin, f- the film
# rect-bay (film.sh, which keeps its frames), l- the lag film bay-sun. The suffix names the side: M2, X2 or A2 (X2 with the clamps off).
T=$(cd "$(dirname "$0")" && pwd); . "$T/vars.sh"
take() { bash "$T/take.sh" "$@"; }
STILLS=rect-lamps,exit-cabin,docking-cabin,cabin-light-cabin,deck-cuts,bay-sunbeam
# Take 1: the stills.
take s-M2 still $STILLS m2
take s-X2 still $STILLS x2
take e-M2 still rect-lamps m2 $E
take e-X2 still rect-lamps x2 $E
take s-A2 still $STILLS x2 $A
take e-A2 still rect-lamps x2 $A $E
# Take 2: the switch cabin.
take w-M2 switch cabin m2
take w-X2 switch cabin x2
# Take 3: the film rect-bay and the lag film bay-sun.
# film.sh keeps the frames for the video, and X2 renders at M2's fixed exposure, so that the video's sides share one.
bash "$T/film.sh" f-M2 rect-bay m2
stops=$(python3 -I -c "import json; print(json.load(open('$W/runs/f-M2/results/motion/rect-bay.json'))['stops'])")
bash "$T/film.sh" f-X2 rect-bay x2 "$stops"
take l-M2 lag bay-sun m2
take l-X2 lag bay-sun x2
echo "$(date +%T) queue done"
