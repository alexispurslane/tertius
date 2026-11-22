#!/usr/bin/env bash
# Tool code of t0-acxm. tracepair.sh <out> <rev...>: one GPU trace of the deck per revision, one after the other: frames
# 399 to 518 of demo-exit (the deck's camera), 1920 x 1080, two frames in flight, no counters. Each revision is extracted
# with git archive into <out>/src-<rev>, whose tools/gpu-trace.sh takes the tree as its root, and it builds it there and records <out>/<rev>/ (passes.md,
# passes.json). Run it inside the dev shell. The trace takes the exclusive GPU lock.
set -uo pipefail
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
out=$1; shift
for rev in "$@"; do
  src=$out/src-$rev
  /bin/rm -rf "$src"; mkdir -p "$src"
  git -C "$R" archive "$rev" | tar -x -C "$src"
  # The extracted tree is no git repository, so the trace's root is the tree itself.
  python3 -I - "$src/tools/gpu-trace.sh" <<'PY'
import sys
p = sys.argv[1]; s = open(p).read()
s = s.replace('root=$(git rev-parse --show-toplevel)', 'root=$(pwd)'); open(p, 'w').write(s)
PY
  # The extracted tree has no built gpu-clock, so it takes this worktree's (tools/README.md, "GPU calibration").
  mkdir -p "$src/captures/gpu-calibration/bin"
  cp "$R/captures/gpu-calibration/bin/gpu-clock" "$src/captures/gpu-calibration/bin/"
  echo "$(date +%T) trace $rev (load $(sysctl -n vm.loadavg))"
  # TERTIUS_DIFFUSE_FILTER=on, the default kernel: gpu-trace.sh passes an empty --env value otherwise, which xctrace
  # refuses (t0-8jfj).
  (cd "$src" && TERTIUS_DIFFUSE_FILTER=on bash tools/gpu-trace.sh --no-counters --start 399 --frames 120 --out "$out/$rev" scripts/demo-exit.json --in-flight 2)
  echo "$(date +%T) end $rev exit $?"
  /bin/rm -rf "$src"
done
