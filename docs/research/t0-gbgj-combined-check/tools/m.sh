#!/usr/bin/env bash
# Tool code of t0-gbgj. m.sh <script> <args...>: runs one measuring script in $W/w (vars.sh), in the dev shell. w/s/ holds a
# link per run folder of $W/runs. A script of this folder wins over the one of t0-9fo0, then t0-acxm, t0-xdck, t0-ion0,
# t0-xlcu, t0-y46f and t0-mzhr.
. "$(dirname "$0")/vars.sh"
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
mkdir -p "$W/w/s" && for d in "$W"/runs/*; do [ -e "$W/w/s/$(basename "$d")" ] || ln -sfn "$d" "$W/w/s/$(basename "$d")"; done
s=$1; shift
for t in t0-gbgj-combined-check t0-9fo0-room-after-shadow-fix t0-acxm-region-evidence t0-xdck-drift-cut-cost t0-ion0-region-drift t0-xlcu-regional-tolerance t0-y46f-window-18-excess t0-mzhr-regional-test-cycle; do
  f=$R/docs/research/$t/tools/$s; [ -e "$f" ] && break
done
P=$R/docs/research
cd "$W/w" && PYTHONPATH=$P/t0-xlcu-regional-tolerance/tools:$P/t0-y46f-window-18-excess/tools:$P/t0-mzhr-regional-test-cycle/tools nix develop "$R" --command python3 "$f" "$@"
