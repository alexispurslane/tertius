#!/usr/bin/env bash
# Tool code (tools/README.md, "GPU calibration"): a Metal System Trace of FSR's batched measurement,
# GpuCalibrationTests.FsrReportsItsGpuTimePerFrameWhenEightFramesShareASubmission, under the GPU lock through
# tools/gpu-lock.sh and beside gpu-clock. MoltenVK ends its compute encoder at each pipeline barrier, and FSR's Record
# puts a barrier before each pass, so each pass of a frame shows as an encoder of its own. fsr-passes.py then gives each
# pass's GPU time. Run it from the worktree root, in the dev shell, after the Release build of the test project and the
# builds of tools/README.md, "GPU calibration":
#   nix develop --command bash tools/gpu-calibration/fsr-trace.sh <out folder>
# It writes <out>/run.trace, <out>/run.log with the test's output and gpu-clock's samples, and <out>/passes.md.
set -uo pipefail

out=${1:?usage: fsr-trace.sh <out folder>}
bin=${GPU_CALIBRATION_BIN:-captures/gpu-calibration/bin}
tests=tests/Tertius.Rendering.Tests/bin/Release/net11.0/Tertius.Rendering.Tests
if [[ -e "$out/run.trace" ]]; then
    echo "$out/run.trace exists. Remove the folder or pass another out folder." >&2
    exit 2
fi
mkdir -p "$out"

# xctrace starts the command from a clean environment, and macOS strips DYLD_* from a process that /usr/bin starts, so
# each variable that the test needs from the dev shell goes through --env.
env_args=(--env TERTIUS_GPU_CALIBRATION=1)
for name in DOTNET_ROOT VK_ICD_FILENAMES DYLD_FALLBACK_LIBRARY_PATH; do
    env_args+=(--env "$name=${!name}")
done
bash tools/gpu-lock.sh "$out/record.log" /usr/bin/xcrun xctrace record --template "Metal System Trace" --no-prompt \
    --output "$out/run.trace" "${env_args[@]}" --target-stdout "$out/run.log" \
    --launch -- "$bin/gpu-clock" 100 "$tests" -noColor -showLiveOutput \
    -method Tertius.Rendering.Tests.GpuCalibrationTests.FsrReportsItsGpuTimePerFrameWhenEightFramesShareASubmission
code=$?
if [[ "$code" -ne 0 ]]; then
    echo "The traced run failed (exit code $code). The end of $out/record.log and $out/run.log:" >&2
    tail -5 "$out/record.log" "$out/run.log" >&2
    exit 1
fi
python3 -I tools/gpu-calibration/fsr-passes.py "$out/run.trace" "$out" | tee "$out/passes.md"
