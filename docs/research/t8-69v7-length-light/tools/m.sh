#!/usr/bin/env bash
set -o pipefail
# m.sh <script> <args...>: runs one measuring script in /private/tmp/t8-69v7/w, in the dev shell. It looks for the script
# in this folder's tools/, then in t0-y46f's, then in t0-mzhr's. w/s/ holds a link per run folder.
cd /private/tmp/t8-69v7 && mkdir -p w/s && for d in runs/*; do [ -e w/s/$(basename $d) ] || ln -sfn ../../$d w/s/$(basename $d); done
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59/docs/research
s=$1; shift
for d in t8-69v7-length-light t0-y46f-window-18-excess t0-mzhr-regional-test-cycle; do
  f=$R/$d/tools/$s; [ -e "$f" ] && break
done
cd w && PYTHONPATH=$R/t0-y46f-window-18-excess/tools nix develop /Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59 --command python3 "$f" "$@" 2>&1 | { grep -v 'warning:\|SQLite' || true; }
