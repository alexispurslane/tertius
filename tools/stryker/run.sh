#!/usr/bin/env bash
# Tool code. Runs Stryker.NET on one source project with the configuration file that tools/stryker/config.py wrote.
# Run it inside the dev shell. Run one Stryker at a time: two runs compete for the CPU and both time out.
#   nix develop --command bash -c 'bash tools/stryker/run.sh Core /tmp/stryker/Core'
# The log goes to <out>/out/logs, because the progress bar shows only on a terminal.
# The JSON report goes to <out>/out/reports/mutation-report.json. Read it with tools/stryker/survivors.py.
set -euo pipefail
# macOS removes the DYLD_* variables when it starts a protected program such as /usr/bin/env, which the #! line runs.
# Without DYLD_FALLBACK_LIBRARY_PATH, the renderer cannot find libvulkan, and every GPU test fails. The dev shell's
# bash keeps the variables, so start the script with bash.
if [[ "$(uname)" == Darwin && -z "${DYLD_FALLBACK_LIBRARY_PATH:-}" ]]; then
  echo "DYLD_FALLBACK_LIBRARY_PATH is empty. Start the script with bash inside the dev shell: bash $0 ..." >&2
  exit 1
fi
project=${1:?usage: tools/stryker/run.sh <module> <out-dir>}
out=$(cd "${2:?usage: tools/stryker/run.sh <module> <out-dir>}" && pwd)
root=$(git rev-parse --show-toplevel)
rm -rf "$out/out"
cd "$root/src/Tertius.$project"
dotnet tool run dotnet-stryker -- -f "$out/stryker-config.json" -O "$out/out" -L
