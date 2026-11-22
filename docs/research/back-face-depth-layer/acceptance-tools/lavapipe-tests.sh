#!/usr/bin/env bash
# Tool code (t1-8b4w): the Rendering tests on Mesa's lavapipe, Mesa's Vulkan driver that runs on the CPU, in one cloud
# job of tools/runpod/gpu_job.py. Run it from the job's work folder:
#   bash t/docs/research/back-face-depth-layer/acceptance-tools/lavapipe-tests.sh <tree> <out> [runner arguments]
# <tree> is the job's tree folder, <out> the folder that the job sends back. The runner arguments go to the xunit
# runner, for example "-class Tertius.Rendering.Tests.PyramidRenderTests" or the output of tools/runpod/shards.py.
#
# The worker's dev shell has no Linux Vulkan driver apart from NVIDIA's (docs/research/remote-runners-2026-11-01.md).
# So the script builds the pinned nixpkgs' mesa, which a substitute from the binary cache gives, and names its lavapipe
# manifest in VK_ICD_FILENAMES. A Nix-built driver links Nix's glibc, as the dev shell's dotnet does. Ubuntu's own
# mesa package would need the system library folder on the search path, which replaces Nix's libraries.
set -uo pipefail
tree=$1; out=$2; shift 2
mkdir -p "$out"
out=$(cd "$out" && pwd); tree=$(cd "$tree" && pwd)
started=$(date +%s)
stamp() { echo "$(( $(date +%s) - started )) s: $*" | tee -a "$out/steps.txt"; }

stamp "start, $(nproc) processors"
nixpkgs=$(nix eval --raw --impure --expr '(builtins.getFlake "path:/opt/tertius-flake").inputs.nixpkgs.outPath' \
  2> "$out/nix-eval.log") || { stamp "nix eval failed"; tail -20 "$out/nix-eval.log"; exit 1; }
stamp "nixpkgs $nixpkgs"
mesa=$(nix build --no-link --print-out-paths --impure --expr "(import $nixpkgs {}).mesa" 2> "$out/nix-build.log")
code=$?
tail -5 "$out/nix-build.log"
[[ $code == 0 ]] || { stamp "nix build of mesa failed with $code"; exit 1; }
icd=$(find $mesa/share/vulkan/icd.d -name 'lvp_icd*.json' 2> /dev/null | head -1)
[[ -n $icd ]] || { stamp "mesa $mesa holds no lavapipe manifest"; ls -R $mesa/share | head -40; exit 1; }
stamp "lavapipe manifest $icd"
export VK_ICD_FILENAMES=$icd
vulkaninfo --summary > "$out/vulkaninfo.txt" 2>&1
grep -E "deviceName|driverName|driverInfo|apiVersion" "$out/vulkaninfo.txt" | tee -a "$out/steps.txt"

(cd "$tree" && dotnet build tests/Tertius.Rendering.Tests -c Checked -nologo -v:q 2>&1 | tail -3) | tee -a "$out/steps.txt"
stamp "built"
dll=$tree/tests/Tertius.Rendering.Tests/bin/Checked/net11.0/Tertius.Rendering.Tests.dll
# With CLASS_STEMS="<file stem> ..." and SHARD="<count> <index>", the runner arguments become the classes of the built
# assembly whose outer class is one of the stems, nested ones included, in shard <index> of <count> (shards.py's rule).
if [[ -n ${CLASS_STEMS:-} ]]; then
  read -r count index <<< "$SHARD"
  mapfile -t picked < <(dotnet "$dll" -list classes 2>/dev/null | python3 -I -c '
import sys
sys.path.insert(0, sys.argv[1])
import shards
stems = set(sys.argv[2].split())
names = [n for n in shards.classes_of(sys.stdin.read()) if n.rsplit(".", 1)[-1].split("+")[0] in stems]
for argument in shards.filters(shards.shard(names, int(sys.argv[3]), int(sys.argv[4]))):
    print(argument)
' "$tree/tools/runpod" "$CLASS_STEMS" "$count" "$index")
  set -- "${picked[@]}"
  stamp "shard $index of $count: $(( ${#picked[@]} / 2 )) classes: $(printf '%s ' "${picked[@]}" | sed 's/-class Tertius.Rendering.Tests.//g')"
fi
(cd "$tree" && dotnet "$dll" -maxThreads 2 -longRunning 120 -noColor -result-ctrf "$out/tests.json" "$@" \
  > "$out/tests.log" 2>&1)
stamp "tests exit $?"
grep -E "^\s*(Total|Passed|Failed|Skipped)|Tests? (run|passed)|=== TEST EXECUTION SUMMARY ===" -A6 "$out/tests.log" \
  | tail -8 | tee -a "$out/steps.txt"
