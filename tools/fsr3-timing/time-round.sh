#!/usr/bin/env bash
# Tool code (tools/README.md, "FSR 3.1 timing"): one round of the FSR timing. It runs FSR's batched measurement once
# from each copy under captures/fsr-builds, in an order that rotates by one build per round, each run under the GPU lock
# through tools/gpu-lock.sh and beside gpu-clock through tools/gpu-clock.sh. The measurement submits 8 frames per
# command buffer and keeps two command buffers on the queue, so the GPU does not idle between frames and its clock holds
# from run to run. It prints the markers of
# its 150 timed batches, so each run's log holds the GPU clock over them, and the mean frame time of each batch, which
# analyze.py reads from the log and compares only between runs at the same clock.
# Run it from the worktree root, with the builds in the order of the first round:
#   bash tools/fsr3-timing/time-round.sh <round> <out folder> base control half wave halfwave
# It writes <round>-<build>.log to the out folder, and prints one line per run, which analyze.py reads from rounds.log:
#   bash tools/fsr3-timing/time-round.sh 0 /private/tmp/fsr-timing base control >> /private/tmp/fsr-timing/rounds.log
set -uo pipefail

round=${1:?round number}
out=${2:?out folder}
shift 2
builds=("$@")
n=${#builds[@]}
mkdir -p "$out"

for ((i = 0; i < n; i++)); do
    build=${builds[$(((i + round) % n))]}
    log="$out/$round-$build.log"
    TERTIUS_FSR_TIMING=1 bash tools/gpu-lock.sh "$log" bash tools/gpu-clock.sh \
        "captures/fsr-builds/$build/Tertius.Rendering.Tests" -noColor -showLiveOutput \
        -method Tertius.Rendering.Tests.Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTimePerFrameWhenEightFramesShareASubmission
    code=$?
    up=$(sed -n 's/^uptime-start: //p' "$log")
    waited=$(sed -n 's/^waited: //p' "$log")
    summary=$(grep -o 'median [0-9.]* ms, 10th percentile [0-9.]* ms, 90th percentile [0-9.]* ms' "$log" | head -1)
    clock=$(sed -n 's/^GPU clock over the timed span: //p' "$log")
    echo "round=$round order=$i build=$build exit=$code waited=$waited uptime=[$up] $summary clock=[$clock]"
done
