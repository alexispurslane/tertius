#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-mac3.sh: the Mac's takes after queue-mac2.sh, asked by the line orchestrator for the walk
# cabin-walk: the frames of the walk's end and of the stop on M and X (walkframes.sh, for walkvideo.sh's video), and the
# walk on X0, the line without the remedy (build x0 of queue-mac2.sh), as a survey walk take.
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-5tyj
bash "$T/walkframes.sh" M $BASE/m/probe-bin
bash "$T/walkframes.sh" X $BASE/x/probe-bin
PROBE=$BASE/x0/probe-bin bash "$T/take.sh" k-X0 walk cabin-walk
echo "$(date +%T) queue-mac3 done"
