#!/usr/bin/env bash
# Tool code of t1-yi11's production check. build.sh <name> <commit>: archives <commit> of the worktree into
# /private/tmp/t1-yi11-prod/<name>/src, applies that commit's tools/renderer-survey/probe.patch and harness.py, builds
# src/Tertius.Demo in Release into /private/tmp/t1-yi11-prod/<name>/probe-bin, and deletes the source tree.
set -euo pipefail
name=$1; commit=$2
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144
out=/private/tmp/t1-yi11-prod/$name
free=$(df -Pk / | awk 'NR == 2 {print int($4 / 1048576)}')
if [ "$free" -lt 22 ]; then echo "only $free GiB free: no build"; exit 1; fi
command rm -rf "$out/src" && mkdir -p "$out/src"
git -C "$R" archive "$commit" | tar -x -C "$out/src"
(cd "$out/src" && patch -p1 -s --forward -i tools/renderer-survey/probe.patch)
python3 -I "$(dirname "$0")/harness.py" "$out/src"
echo "$commit probe.patch harness.py" > "$out/build.txt"
cd "$out/src" && nix develop "$R" --command dotnet build src/Tertius.Demo -c Release -v quiet -nologo \
  -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o "$out/probe-bin"
command rm -rf "$out/src"
echo "$out/probe-bin/Tertius.Demo"
