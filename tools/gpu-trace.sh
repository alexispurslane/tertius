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
# Arguments after the script go to the demo's headless run, such as --screen-space-light off.
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
while [[ $# -gt 0 ]]; do
  case "$1" in
    --start) start=$2; shift 2 ;;
    --frames) frames=$2; shift 2 ;;
    --size) size=$2; shift 2 ;;
    --out) out=$2; shift 2 ;;
    --no-counters) counters=0; shift ;;
    --keep-exports) keep=1; shift ;;
    --window) window=(--window "${2:?--window takes two pass names}" "${3:?--window takes two pass names}"); shift 3 ;;
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
dotnet build src/Tertius.Demo -c Release -v quiet -nologo > "$out/build.log" 2>&1 || { tail -20 "$out/build.log" >&2; exit 1; }
demo=src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo

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
load=$(uptime | sed 's/.*load averages*: *//')
echo "Load averages at the start (1, 5 and 15 minutes): $load"
"$xcrun" xctrace record --template "Metal System Trace" "${instruments[@]}" --no-prompt --output "$out/run.trace" \
  "${env_args[@]}" --target-stdout "$out/run.log" \
  --launch -- "$demo" --script "$traced" --headless --out "$out/run" --size "$size" --video none "${demo_options[@]}" \
  > "$out/record.log" 2>&1 || record_code=$?
if [[ "$record_code" -ne 0 ]]; then
  # xctrace saves the trace and exits with a code other than 0 when the demo fails, and the demo's errors go to run.log.
  if [[ -e "$out/run.trace" ]]; then
    echo "The demo run failed (xctrace exited with $record_code). The end of $out/run.log:" >&2
    tail -20 "$out/run.log" >&2
  else
    echo "xctrace recorded no trace (exit code $record_code). The end of $out/record.log:" >&2
    tail -20 "$out/record.log" >&2
  fi
  exit 1
fi
if [[ ! -f "$out/run/gpu-timings.json" ]]; then
  echo "The run wrote no gpu-timings.json. Its output is in $out/run.log." >&2
  exit 1
fi
grep -E '^GPU frame span' "$out/run.log" || true

python3 -I tools/gpu-trace.py report "$out/run.trace" "$out/run/gpu-timings.json" "$out" "${window[@]}"
if [[ "$keep" -eq 0 ]]; then
  rm -rf "$out/export"
fi
printf 'Load averages at the start: %s\n\n%s\n' "$load" "$(cat "$out/passes.md")" > "$out/passes.md"
echo "$out/passes.md"
