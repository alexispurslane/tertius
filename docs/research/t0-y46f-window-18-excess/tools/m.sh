#!/usr/bin/env bash
# m.sh <script> <args...>: runs one measuring script of the research folder in /private/tmp/t0-y46f/w, in the dev shell.
cd /private/tmp/t0-y46f && for d in runs/*; do [ -e w/s/$(basename $d) ] || ln -sfn ../../$d w/s/$(basename $d); done
T=/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59/docs/research/t0-y46f-window-18-excess/tools
O=/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59/docs/research/t0-mzhr-regional-test-cycle/tools
s=$1; shift; f=$T/$s; [ -e "$f" ] || f=$O/$s
cd w && nix develop /Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59 --command python3 "$f" "$@"
