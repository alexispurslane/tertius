#!/usr/bin/env bash
# Tool code (tools/README.md, "The GPU clock of a timed run"): runs one command beside gpu-clock, which samples the GPU's
# clock every 100 ms, and then prints the clock over the command's timed span. It is the one launcher of gpu-clock for
# every timing tool.
#   bash tools/gpu-clock.sh <command> [arguments]
# The command prints the lines timed-start and timed-end around its timed work. Its output and gpu-clock's samples go to
# stdout, in the order in which they happen. The last line is "GPU clock over the timed span: ..." from
# tools/gpu_clock.py. The exit code is the command's.
#
# gpu-clock reads IOReport, which only macOS has, and comes from $GPU_CALIBRATION_BIN, by default
# captures/gpu-calibration/bin of this checkout (tools/README.md, "GPU calibration", builds it). Where it cannot run, the
# script prints "clock unknown: <reason>" and runs the command alone, so the tool still works and its record says that the
# clock is unknown. gpu-clock itself prints the same line and runs the command alone when IOReport gives no GPU clock.
set -uo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
bin=${GPU_CALIBRATION_BIN:-$root/captures/gpu-calibration/bin}
reason=
if [[ "$(uname)" != Darwin ]]; then
    reason="gpu-clock reads IOReport, which only macOS has"
elif [[ ! -x "$bin/gpu-clock" ]]; then
    reason="$bin/gpu-clock is not built (tools/README.md, \"GPU calibration\")"
fi

copy=$(mktemp "${TMPDIR:-/tmp}/gpu-clock.XXXXXX")
trap 'rm -f "$copy"' EXIT
if [[ -n "$reason" ]]; then
    { echo "clock unknown: $reason"; "$@"; } | tee "$copy"
else
    "$bin/gpu-clock" 100 "$@" | tee "$copy"
fi
code=${PIPESTATUS[0]}
python3 -I "$root/tools/gpu_clock.py" span "$copy"
exit "$code"
