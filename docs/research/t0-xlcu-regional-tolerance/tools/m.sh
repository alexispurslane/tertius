#!/usr/bin/env bash
# m.sh <script> <args...>: runs one measuring script in /private/tmp/t0-xlcu/w, in the dev shell. w/s/ holds a link per
# run folder. A script of this folder wins over the one of t0-y46f, which wins over the one of t0-mzhr.
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
cd /private/tmp/t0-xlcu && mkdir -p w/s && for d in runs/*; do [ -e w/s/$(basename $d) ] || ln -sfn ../../$d w/s/$(basename $d); done
s=$1; shift
for t in t0-xlcu-regional-tolerance t0-y46f-window-18-excess t0-mzhr-regional-test-cycle; do
  f=$R/docs/research/$t/tools/$s; [ -e "$f" ] && break
done
cd w && PYTHONPATH=$R/docs/research/t0-y46f-window-18-excess/tools:$R/docs/research/t0-mzhr-regional-test-cycle/tools nix develop $R --command python3 "$f" "$@"
