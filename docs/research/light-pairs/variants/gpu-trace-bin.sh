#!/usr/bin/env bash
# Tool code, Mac only. Records a Metal System Trace of a headless demo run with xctrace, and writes a table of each
# pass's GPU time and limiters (tools/README.md, "GPU trace"). No engine code depends on it. Run it inside the dev
# shell, from anywhere in the repository:
#   nix develop --command bash -c 'bash tools/gpu-trace.sh scripts/demo-exit.json'
#   nix develop --command bash -c 'bash tools/gpu-trace.sh --start 399 --frames 120 scripts/demo-exit.json'
# Options:
#   --start <frame>   the first frame to trace, counted from 0 over the script's frames (default 0); the frames
#                     before it run without a render
#   --frames <count>  how many frames to trace (default 120; 0 traces every frame from --start to the end of the
#                     script, whose counters can export to many gigabytes)
#   --size <WxH>      the size of the frames (default 1920x1080)
#   --out <dir>       the folder of the trace (default captures/gpu-trace/<script>-<start>)
#   --no-counters     records no GPU counters: a smaller trace with times and no limiters
#   --keep-exports    keeps the XML tables that the report exports from the trace, in <out>/export
#   --window <from> <to>
#                     adds a row with each frame's wall-clock GPU time from the end of pass <from> to the start of
#                     pass <to>
#   --paired <pass>   adds the paired table, for a run whose frames alternate with and without <pass>
#   --control <pass>  adds the paired table's control, for a run whose frames all run <pass>
#                     (give --paired or --control once, not both)
# Arguments after the script go to the demo's headless run, such as --screen-trace off.
# The recording takes the GPU lock through tools/gpu-lock.sh and runs beside gpu-clock through tools/gpu-clock.sh. The
# clock over the run's timed span goes into gpu-timings.json as "gpuClock" and into passes.md. <out>/run.log holds the
# lock's uptime lines, xctrace's messages, gpu-clock's samples and the demo's output.
# Writes <out>/run.trace (open it in Instruments), <out>/run (the run's gpu-timings.json and exposure.json; --video none
# draws each capture and writes no image),
# <out>/passes.md (the table, also printed) and <out>/passes.json (every counter of every pass).
set -euo pipefail
if [[ "$(uname)" != Darwin ]]; then
  echo "gpu-trace.sh records with Xcode's xctrace, which runs only on macOS." >&2
  exit 2
fi
if [[ -z "${TERTIUS_VULKAN_DIR:-}" ]]; then
  echo "TERTIUS_VULKAN_DIR is empty. Run the script inside the dev shell: nix develop --command bash $0 ..." >&2
  exit 2
fi
# The dev shell puts xcbuild's xcrun first on the PATH, and it has no xctrace.
xcrun=/usr/bin/xcrun
if ! "$xcrun" --find xctrace > /dev/null 2>&1; then
  echo "gpu-trace.sh needs Xcode's xctrace, which the Command Line Tools lack (xcode-select -s /Applications/Xcode.app)." >&2
  exit 2
fi
start=0
frames=120
size=1920x1080
out=
counters=1
keep=0
window=()
pairing=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --start) start=$2; shift 2 ;;
    --frames) frames=$2; shift 2 ;;
    --size) size=$2; shift 2 ;;
    --out) out=$2; shift 2 ;;
    --no-counters) counters=0; shift ;;
    --keep-exports) keep=1; shift ;;
    --window) window=(--window "${2:?--window takes two pass names}" "${3:?--window takes two pass names}"); shift 3 ;;
    --paired|--control)
      [[ ${#pairing[@]} -eq 0 ]] || { echo "Give one --paired or one --control, not both and not twice." >&2; exit 2; }
      pairing=("$1" "${2:?$1 takes a pass name}"); shift 2 ;;
    -*) echo "Unknown option $1." >&2; exit 2 ;;
    *) break ;;
  esac
done
script=${1:?usage: tools/gpu-trace.sh [options] <script.json> [demo options]}
shift
demo_options=("$@")
script=$(realpath "$script")
root=$(git rev-parse --show-toplevel)
cd "$root"
out=$(realpath -m "${out:-captures/gpu-trace/$(basename "$script" .json)-$start}")
if [[ -e "$out/run.trace" ]]; then
  echo "$out/run.trace exists. Remove the folder or pass another --out." >&2
  exit 2
fi
mkdir -p "$out"

# The built binary, not dotnet run, so that the trace holds no build. --video none draws and waits for each frame as a
# video run does, and writes no frame, so the frames follow each other as in the cost recipe, with no PNG encode between.
: # no build: the binary of TRACE_DEMO_BIN
demo=${TRACE_DEMO_BIN:?}

traced="$out/script.json"
python3 -I tools/gpu-trace.py trim "$script" "$start" "$frames" "$traced"

# xctrace starts the demo from a clean environment, and macOS strips DYLD_* from a process that /usr/bin starts, so
# each variable that the demo needs from the dev shell goes through --env.
env_args=()
for name in DOTNET_ROOT VK_ICD_FILENAMES DYLD_FALLBACK_LIBRARY_PATH; do
  env_args+=(--env "$name=${!name}")
done
instruments=()
if [[ "$counters" -eq 1 ]]; then
  # The counter set "Performance Limiters", sampled about every 10 µs across the whole GPU.
  instruments=(--instrument "Metal GPU Counters")
fi
record_code=0
# The recording takes the GPU lock (tools/README.md, "The GPU lock") and runs beside gpu-clock through
# tools/gpu-clock.sh. The demo must be the process that xctrace launches: with a parent between them, the trace's list
# of encoders stays empty. So gpu-clock samples outside xctrace, and xctrace passes the demo's output on to its own
# stdout (--target-stdout -), which keeps the demo's markers in order between gpu-clock's samples in run.log.
bash tools/gpu-lock.sh "$out/run.log" bash tools/gpu-clock.sh "$xcrun" xctrace record --template "Metal System Trace" \
  "${instruments[@]}" --no-prompt --output "$out/run.trace" "${env_args[@]}" --target-stdout - \
  --launch -- "$demo" --script "$traced" --headless --out "$out/run" --size "$size" --video none "${demo_options[@]}" \
  || record_code=$?
if [[ "$record_code" -ne 0 ]]; then
  # xctrace saves the trace and exits with a code other than 0 when the demo fails.
  if [[ -e "$out/run.trace" ]]; then
    echo "The demo run failed (xctrace exited with $record_code). The end of $out/run.log:" >&2
  else
    echo "xctrace recorded no trace (exit code $record_code). The end of $out/run.log:" >&2
  fi
  tail -20 "$out/run.log" >&2
  exit 1
fi
if [[ ! -f "$out/run/gpu-timings.json" ]]; then
  echo "The run wrote no gpu-timings.json. Its output is in $out/run.log." >&2
  exit 1
fi
grep -E '^GPU frame span' "$out/run.log" || true
python3 -I tools/gpu_clock.py record "$out/run.log" "$out/run/gpu-timings.json"

# The report takes the load at the start from the lock's line uptime-start in run.log, which comes after any wait for the
# lock.
python3 -I tools/gpu-trace.py report "$out/run.trace" "$out/run/gpu-timings.json" "$out" --log "$out/run.log" \
  "${window[@]}" "${pairing[@]}"
if [[ "$keep" -eq 0 ]]; then
  rm -rf "$out/export"
fi
echo "$out/passes.md"
