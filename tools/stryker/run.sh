#!/usr/bin/env bash
# Tool code. Runs Stryker.NET on one source project with the configuration file that tools/stryker/config.py wrote.
# Run it inside the dev shell. Run one Stryker at a time: two runs compete for the CPU and both time out.
#   nix develop --command bash -c 'tools/stryker/run.sh Core /tmp/stryker/Core'
# The log goes to <out>/out/logs, because the progress bar shows only on a terminal.
# The JSON report goes to <out>/out/reports/mutation-report.json. Read it with tools/stryker/survivors.py.
set -euo pipefail
project=${1:?usage: tools/stryker/run.sh <module> <out-dir>}
out=$(cd "${2:?usage: tools/stryker/run.sh <module> <out-dir>}" && pwd)
root=$(git rev-parse --show-toplevel)
rm -rf "$out/out"
cd "$root/src/Tertius.$project"
dotnet tool run dotnet-stryker -- -f "$out/stryker-config.json" -O "$out/out" -L
