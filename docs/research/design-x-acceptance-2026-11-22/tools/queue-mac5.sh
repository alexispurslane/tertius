#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-mac5.sh: the walk's frames as pictures for the video (walkframes.sh), M and X, after
# queue-mac4.sh.
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-5tyj
bash "$T/walkframes.sh" M $BASE/m/probe-bin
bash "$T/walkframes.sh" X $BASE/x/probe-bin
echo "$(date +%T) queue-mac5 done"
