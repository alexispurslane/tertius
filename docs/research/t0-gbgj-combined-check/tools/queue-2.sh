#!/usr/bin/env bash
# Tool code of t0-gbgj. queue-2.sh: the take that the line orchestrator added after queue.sh: the film bay-sunbeam on M2
# and X2 through film.sh, which keeps the frames for the video and for the slit's teeth (bug tc-6v3d). X2 renders at
# M2's fixed exposure, so that the video's sides share one.
T=$(cd "$(dirname "$0")" && pwd); . "$T/vars.sh"
bash "$T/film.sh" fs-M2 bay-sunbeam m2
stops=$(python3 -I -c "import json; print(json.load(open('$W/runs/fs-M2/results/motion/bay-sunbeam.json'))['stops'])")
bash "$T/film.sh" fs-X2 bay-sunbeam x2 "$stops"
echo "$(date +%T) queue-2 done"
