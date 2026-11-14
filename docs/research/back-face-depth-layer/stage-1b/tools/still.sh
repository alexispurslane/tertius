#!/usr/bin/env bash
# Throwaway (t1-n8x9): one headless run of a frozen binary under the shared GPU lock.
# Run inside the dev shell from the worktree: bash still.sh <binary folder> <out> <mode> <script> [demo arguments]
set -uo pipefail
bin=$1; out=$2; mode=$3; script=$4; shift 4
lock=/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh
mkdir -p "$out"
TERTIUS_BACK_FACE_PROBE=$mode bash "$lock" --shared "$out/run.log" "$bin/Tertius.Demo" \
  --script "$( [[ $script == *.json ]] && echo "$script" || echo "scripts/$script.json")" --headless --out "$out" "$@"
echo "exit $?" >> "$out/run.log"
