#!/usr/bin/env bash
# Tool code of t0-rs55. cardwide-submit.sh <job name> <wide commit> [target ms] [runs]: one cloud job on the RTX 2000 Ada,
# submitted only through main's tools/runpod/gpu_job.py (GPU_JOB, the line orchestrator's rule), which enforces the spending
# caps. The trees: main = main 86ae0fbc8 (today's fix) and wide = the wide fix's commit, which holds this record's
# cardwide.sh; cardwide.sh does the job's work (its head says what). Outputs land in /private/tmp/t0-gbgj/cloud-rs55/<job
# name>, the log beside it. The defaults: 16.6667 ms and 2 rounds.
set -uo pipefail
R=$(cd "$(dirname "$0")/../../../.." && pwd)
C=/private/tmp/t0-gbgj/cloud-rs55; mkdir -p $C
GPU_JOB=${GPU_JOB:-/Users/alexispurslane/Development/tertius/tools/runpod/gpu_job.py}
name=$1; wide=$2; target=${3:-16.6667}; runs=${4:-2}
(cd "$R" && python3 -I "$GPU_JOB" submit --agent t0-rs55 --tree main=86ae0fbc8 --tree wide=$wide \
  --run "bash wide/docs/research/t0-rs55-wide-fix/tools/cardwide.sh $target $runs" \
  --output 'out/card/*/*.txt' --timeout 1500 --out "$C/$name") > "$C/$name.log" 2>&1
echo "$(date +%T) exit $? $name: $(tail -1 "$C/$name.log" | cut -c1-300)"
