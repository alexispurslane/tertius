#!/usr/bin/env bash
# Tool code of t0-gumm. card.sh <job name> <target ms> <code...>: one cloud job on the RTX 2000 Ada through
# tools/runpod/gpu_job.py only, which enforces the spending caps; cardrun.sh does the job's work (its head lists the codes
# and the runs). The tree x is X5 as the commit 4851ba7db (tree e232080f, the line's merge 710834cc0). The worker checks
# out no docs/, so the first command unpacks this record's patches, cut.json and cardrun.sh, t0-3vwx's scratch patches and
# the acceptance record's scratch-light-evidence-cap.patch (read by its path) into x, as a base64 tar. Outputs land in
# /private/tmp/t0-gbgj/cloud-gumm/<job name>, the log beside it.
# Example: bash card.sh shapes1 16.6667 1,8,0 0.25,32,0 0.125,32,0 0.0625,32,0 cap
set -uo pipefail
R=$(cd "$(dirname "$0")/../../../.." && pwd)
D=docs/research/t0-gumm-catch-up-spread; V=docs/research/t0-3vwx-dynamic-scale-films
A=/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22
C=/private/tmp/t0-gbgj/cloud-gumm; mkdir -p $C
X=4851ba7db147563e5b8a94856710dd5c37efe2d6
name=$1; shift
stage=$(mktemp -d); mkdir -p $stage/$D/tools $stage/$V
cp "$R/$D"/scratch-*.patch "$R/$D"/cut.json $stage/$D/; cp "$R/$D"/tools/cardrun.sh $stage/$D/tools/
cp "$R/$V"/scratch-region-log.patch "$R/$V"/scratch-dynamic-scale.patch "$R/$V"/scratch-history-log.patch $stage/$V/
cp "$A"/scratch-light-evidence-cap.patch $stage/$D/
pack=$(COPYFILE_DISABLE=1 tar -czf - -C $stage docs | base64 | tr -d '\n'); rm -rf $stage
(cd "$R" && python3 -I tools/runpod/gpu_job.py submit --agent t0-gumm --tree x=$X --run "echo $pack | base64 -d | tar -xz -C x" \
  --run "bash x/$D/tools/cardrun.sh $*" --output 'out/card/*/*.txt' --timeout 1200 --out "$C/$name") > "$C/$name.log" 2>&1
echo "$(date +%T) exit $? $name: $(tail -1 "$C/$name.log" | cut -c1-300)"
