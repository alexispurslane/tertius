#!/usr/bin/env bash
# Tool code of t0-gbgj. build.sh <name> <rev> [patch ...]: one build of a side, as the survey's probe-build makes it, from a
# revision and patches instead of the checkout's head. It extracts <rev> with git archive into $W/src-<name>, applies each
# patch in order (a patch given as <rev>:<path> is read from that revision), builds the Release demo without the
# analyzers into $W/bins/<name>/probe-bin, and deletes the source tree. With PLAIN=1 it builds into $W/bins/<name>/plain,
# the timing build of round.sh (no patch is needed then). It stops below MIN_GIB of free disk: 40 by the brief, 32 for the
# two builds of the glow switch (the line orchestrator's floor of 2026-11-22).
set -euo pipefail
. "$(dirname "$0")/vars.sh"
name=$1; rev=$2; shift 2
R=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
free=$(df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}')
if [ "$free" -lt "${MIN_GIB:-40}" ]; then echo "only $free GiB free: no build"; exit 1; fi
src=$W/src-$name
/bin/rm -rf "$src"; mkdir -p "$src"
git -C "$R" archive "$rev" | tar -x -C "$src"
for p in "$@"; do
  if [[ "$p" == *:* && ! -e "$p" ]]; then git -C "$R" show "$p" > "$src.patch"; p=$src.patch; fi
  patch -d "$src" -p1 --forward -s -i "$(cd "$(dirname "$p")" && pwd)/$(basename "$p")"
done
if [ "${PLAIN:-0}" = 1 ]; then bin=$W/bins/$name/plain; else bin=$W/bins/$name/probe-bin; fi
(cd "$src" && nix develop "$R" --command dotnet build src/Tertius.Demo -c Release -v quiet -nologo -p:RunAnalyzers=false \
  -p:TreatWarningsAsErrors=false -o "$bin")
/bin/rm -rf "$src" "$src.patch"
echo "$(git -C "$R" rev-parse "$rev") $* plain=${PLAIN:-0}" > "$(dirname "$bin")/built-$(basename "$bin").txt"
echo "$bin"
