#!/usr/bin/env bash
# Research tool (issue #42): runs a list of render-variant.sh calls one after another, with logs in captures/issue42/logs/.
#   nix develop --command bash docs/research/restraint-per-surface/tools/take.sh '<variant> <gain> <set>...' ...
# render-variant.sh skips finished cases, so a take can run again after a restart.
set -u
tools="$(cd "$(dirname "$0")" && pwd)"
logs="$PWD/captures/issue42/logs"
mkdir -p "$logs"
for args in "$@"; do
    echo "$(date +%T) start $args"
    bash "$tools/render-variant.sh" $args > "$logs/${args// /_}.log" 2>&1 || echo "FAILED $args (see $logs/${args// /_}.log)"
done
echo "$(date +%T) take done"
