#!/usr/bin/env bash
# Tool code of t0-xdck. timing-queue.sh: the timing rounds in turn, each order then its reverse.
D=$(dirname "$0")
bash $D/round.sh r1 M X Xt D4 D8 D4s C4
bash $D/round.sh r2 C4 D4s D8 D4 Xt X M
bash $D/round.sh r3 M X Xt D4 D8 D4s C4
bash $D/round.sh r4 C4 D4s D8 D4 Xt X M
