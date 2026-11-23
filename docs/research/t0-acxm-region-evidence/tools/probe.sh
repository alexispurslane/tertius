#!/usr/bin/env bash
# Tool code of t0-acxm. probe.sh <rev> <name> [patch ...]: builds one side for the takes. It extracts <rev> with git
# archive, applies that tree's tools/renderer-survey/probe.patch and then each extra patch, and builds the Release demo
# into $W/bins/<name>/probe-bin (the survey's probe). With PLAIN=1 it skips the probe patch and builds into
# $W/bins/<name>, for the timing rounds (round.sh). PROBE_PATCH names another probe patch: the remedy's sides take
# probe-acxm.patch, the survey's probe fitted to the remedy's push constants. $W is the takes folder of vars.sh. Run it
# inside the dev shell.
set -euo pipefail
. "$(dirname "$0")/vars.sh"
rev=$1; name=$2; shift 2
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
src=$W/src-$name
/bin/rm -rf "$src"; mkdir -p "$src"
git -C "$R" archive "$rev" | tar -x -C "$src"
[ "${PLAIN:-0}" = 1 ] || patch -d "$src" -p1 --forward -s -i "${PROBE_PATCH:-$src/tools/renderer-survey/probe.patch}"
for p in "$@"; do patch -d "$src" -p1 --forward -s -i "$(cd "$(dirname "$p")" && pwd)/$(basename "$p")"; done
if [ "${PLAIN:-0}" = 1 ]; then bin=$W/bins/$name; else bin=$W/bins/$name/probe-bin; fi
(cd "$src" && dotnet build src/Tertius.Demo -c Release -v quiet -nologo -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o "$bin")
/bin/rm -rf "$src"
echo "$rev $* -> $bin" > "$(dirname "$bin")/built.txt"
echo "$bin"
