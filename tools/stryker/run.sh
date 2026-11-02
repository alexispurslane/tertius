#!/usr/bin/env bash
# Tool code. Runs Stryker.NET on one source project with the configuration file that tools/stryker/config.py wrote.
# Run it inside the dev shell, under the lock /tmp/tertius-stryker.lock: two runs compete for the CPU and both time
# out. tools/stryker/changed.py, the official run, takes the lock and calls this script (tools/README.md).
#   nix develop --command bash -c 'bash tools/stryker/run.sh Core /tmp/stryker/Core'
# The log goes to <out>/out/logs, because the progress bar shows only on a terminal.
# The JSON report goes to <out>/out/reports/mutation-report.json. Read it with tools/stryker/survivors.py.
set -euo pipefail
# The build copies the Jolt library and the Vulkan loader next to each output from the dev shell's variables, so
# the tests need no DYLD_* variable. Without the dev shell, the GPU tests cannot find libvulkan.
if [[ -z "${TERTIUS_VULKAN_DIR:-}" ]]; then
  echo "TERTIUS_VULKAN_DIR is empty. Run the script inside the dev shell: nix develop --command bash $0 ..." >&2
  exit 1
fi
project=${1:?usage: tools/stryker/run.sh <module> <out-dir>}
out=$(cd "${2:?usage: tools/stryker/run.sh <module> <out-dir>}" && pwd)
root=$(git rev-parse --show-toplevel)
rm -rf "$out/out"
cd "$root/src/Tertius.$project"
dotnet tool run dotnet-stryker -- -f "$out/stryker-config.json" -O "$out/out" -L
