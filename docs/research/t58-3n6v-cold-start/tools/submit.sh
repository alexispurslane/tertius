#!/usr/bin/env bash
# Tool code of t58-3n6v. submit.sh [job name]: one card job of cold.sh, submitted only through main's
# tools/runpod/gpu_job.py, on the tree main = 2e8fdaed2. The worker checks out no docs/, so the first command unpacks the
# folder job: cold.sh, cold.json, the ter-bcwr record's scratch-takes-build.patch (1-takes.patch) and this record's
# pass-log.patch (2-pass-log.patch). Outputs land in /private/tmp/t0-gbgj/cloud-bcwr/<job name>, the log beside it.
set -uo pipefail
T=$(cd "$(dirname "$0")" && pwd); R=$(cd "$T/../../../.." && pwd)
C=/private/tmp/t0-gbgj/cloud-bcwr; name=${1:-cold1}; mkdir -p $C
GPU_JOB=${GPU_JOB:-/Users/alexispurslane/Development/tertius/tools/runpod/gpu_job.py}
stage=$(mktemp -d); mkdir -p $stage/job
cp $T/cold.sh $T/cold.json $stage/job/
cp $R/docs/research/ter-bcwr-announced-dip/scratch-takes-build.patch $stage/job/1-takes.patch
cp $T/../pass-log.patch $stage/job/2-pass-log.patch
pack=$(cd $stage && COPYFILE_DISABLE=1 tar -czf - job | base64 | tr -d '\n'); rm -rf $stage
(cd "$R" && python3 -I "$GPU_JOB" submit --agent t58-3n6v --tree main=2e8fdaed2 \
  --run "echo $pack | base64 -d | tar -xz" --run "bash job/cold.sh" \
  --output 'out/card/*.txt' --output 'out/card/*/*.txt' --timeout 1500 --out "$C/$name") > "$C/$name.log" 2>&1
echo "$(date +%T) exit $? $name: $(tail -1 "$C/$name.log" | cut -c1-300)"
