#!/usr/bin/env bash
# Tool code of t0-9fo0, part B. build.sh <name> <commit> <patch>...: one probe build, as the survey's probe-build makes it,
# from a revision and patches instead of the checkout's head. It archives <commit> into $BASE/<name>/src (BASE: default
# /private/tmp/t0-9fo0b), applies each patch in order, builds src/Tertius.Demo in Release into $BASE/<name>/probe-bin,
# and deletes the source tree. A patch given as <commit>:<path> is read from that
# revision, so that X0 and M take the survey probe of their own tree. It stops below 40 GiB of free disk.
set -euo pipefail
name=$1; commit=$2; shift 2
R=$(cd "$(dirname "$0")/../../../.." && pwd)
out=${BASE:-/private/tmp/t0-9fo0b}/$name
free=$(df -Pk /System/Volumes/Data | awk 'NR == 2 {print int($4 / 1048576)}')
if [ "$free" -lt 40 ]; then echo "only $free GiB free: no build"; exit 1; fi
command rm -rf "$out/src" && mkdir -p "$out/src"
git -C "$R" archive "$commit" | tar -x -C "$out/src"
for p in "$@"; do
  if [[ "$p" == *:* && ! -e "$p" ]]; then git -C "$R" show "$p" > "$out/patch.tmp"; p="$out/patch.tmp"; fi
  (cd "$out/src" && patch -p1 -s --forward -i "$p")
done
echo "$commit $*" > "$out/build.txt"
cd "$out/src" && nix develop "$R" --command dotnet build src/Tertius.Demo -c Release -v quiet -nologo \
  -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o "$out/probe-bin"
command rm -rf "$out/src" "$out/patch.tmp"
echo "$out/probe-bin/Tertius.Demo"
