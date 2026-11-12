#!/usr/bin/env bash
# Research prototype for ticket ter-s97m: one scene's quality job on the GPU workbench, in the job's work folder with
# the trees main and cut. Applies the survey's probe to both trees (and the roughness view to main), builds both, and
# runs measure.py.
#   bash cut/tools/stbn/job.sh <case> [<crop x> <crop y>]
set -euo pipefail
# This script's folder: the cut tree's in the study, the tools tree's in the build's A/B.
here=$(cd "$(dirname "$0")" && pwd)
(cd main && git apply --whitespace=nowarn tools/renderer-survey/probe.patch \
    && git apply --whitespace=nowarn "$here/probe-roughness.patch")
(cd cut && git apply --whitespace=nowarn tools/renderer-survey/probe.patch)
for tree in main cut; do
    (cd "$tree" && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false | tail -2)
done
python3 -I "$here/measure.py" "$@" out
rm -rf render
