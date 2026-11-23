#!/usr/bin/env bash
# Tool code of ter-bcwr. card-submit.sh <job name> <build commit> [target ms] [runs] [event...]: one cloud job on the RTX
# 2000 Ada, submitted only through main's tools/runpod/gpu_job.py (GPU_JOB, the line orchestrator's rule), which enforces
# the spending caps. The trees: today = c776bed47 (one render scale) and build = the build's commit. The worker checks out
# no docs/, so the first command unpacks card.sh, t0-gumm's start.json, cut.json and scratch-first-filmed.patch, the
# acceptance record's scratch-dynamic-scale-step2.patch and this record's scratch-takes-build.patch into the folder job,
# as a base64 tar: the patches of each code in job/<code>/, in their names' order. card.sh does the job's work (its head
# says what). Outputs land in /private/tmp/t0-gbgj/cloud-bcwr/<job name>, the log beside it. The defaults: 16.6667 ms and
# 2 rounds.
set -uo pipefail
R=$(cd "$(dirname "$0")/../../../.." && pwd)
C=/private/tmp/t0-gbgj/cloud-bcwr; mkdir -p $C
GPU_JOB=${GPU_JOB:-/Users/alexispurslane/Development/tertius/tools/runpod/gpu_job.py}
name=$1; build=$2; target=${3:-16.6667}; runs=${4:-2}; events=${*:5}
stage=$(mktemp -d); mkdir -p $stage/job/today $stage/job/build
G=$R/docs/research/t0-gumm-catch-up-spread
cp "$R/docs/research/ter-bcwr-announced-dip/tools/card.sh" $G/start.json $G/cut.json $stage/job/
cp "$R/docs/research/design-x-acceptance-2026-11-22/scratch-dynamic-scale-step2.patch" $stage/job/today/1-dynamic-scale.patch
cp $G/scratch-first-filmed.patch $stage/job/today/2-first-filmed.patch
cp "$R/docs/research/ter-bcwr-announced-dip/scratch-takes-build.patch" $stage/job/build/1-takes.patch
pack=$(COPYFILE_DISABLE=1 tar -czf - -C $stage job | base64 | tr -d '\n'); rm -rf $stage
(cd "$R" && python3 -I "$GPU_JOB" submit --agent ter-bcwr --tree today=c776bed47 --tree build=$build \
  --run "echo $pack | base64 -d | tar -xz" --run "bash job/card.sh $target $runs $events" \
  --output 'out/card/*/*.txt' --timeout 1500 --out "$C/$name") > "$C/$name.log" 2>&1
echo "$(date +%T) exit $? $name: $(tail -1 "$C/$name.log" | cut -c1-300)"
