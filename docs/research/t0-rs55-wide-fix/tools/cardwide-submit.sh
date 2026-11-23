#!/usr/bin/env bash
# Tool code of t0-rs55. cardwide-submit.sh <job name> <wide commit> [target ms] [runs] [event...]: one cloud job on the RTX 2000 Ada,
# submitted only through main's tools/runpod/gpu_job.py (GPU_JOB, the line orchestrator's rule), which enforces the spending
# caps. The trees: main = main 86ae0fbc8 (today's fix) and wide = the wide fix's commit. The worker checks out no docs/, so
# the first command unpacks cardwide.sh, this record's turn.json and walk.json, t0-gumm's start.json, cut.json and scratch-first-filmed.patch and the acceptance
# record's scratch-dynamic-scale-step2.patch into the folder job, as a base64 tar; cardwide.sh does the job's work (its head
# says what). Outputs land in /private/tmp/t0-gbgj/cloud-rs55/<job
# name>, the log beside it. The defaults: 16.6667 ms and 2 rounds.
set -uo pipefail
R=$(cd "$(dirname "$0")/../../../.." && pwd)
C=/private/tmp/t0-gbgj/cloud-rs55; mkdir -p $C
GPU_JOB=${GPU_JOB:-/Users/alexispurslane/Development/tertius/tools/runpod/gpu_job.py}
name=$1; wide=$2; target=${3:-16.6667}; runs=${4:-2}; events=${*:5}
stage=$(mktemp -d); mkdir -p $stage/job
G=$R/docs/research/t0-gumm-catch-up-spread
cp "$R/docs/research/t0-rs55-wide-fix/tools/cardwide.sh" "$R"/docs/research/t0-rs55-wide-fix/{turn,walk}.json $G/start.json $G/cut.json $G/scratch-first-filmed.patch \
  "$R/docs/research/design-x-acceptance-2026-11-22/scratch-dynamic-scale-step2.patch" $stage/job/
pack=$(COPYFILE_DISABLE=1 tar -czf - -C $stage job | base64 | tr -d '\n'); rm -rf $stage
(cd "$R" && python3 -I "$GPU_JOB" submit --agent t0-rs55 --tree main=86ae0fbc8 --tree wide=$wide \
  --run "echo $pack | base64 -d | tar -xz" --run "bash job/cardwide.sh $target $runs $events" \
  --output 'out/card/*/*.txt' --timeout 1500 --out "$C/$name") > "$C/$name.log" 2>&1
echo "$(date +%T) exit $? $name: $(tail -1 "$C/$name.log" | cut -c1-300)"
