#!/usr/bin/env bash
# Tool code. Runs every test project with code coverage, in each build configuration given, and writes one Cobertura
# report per project to <out>/<configuration>/<module>/<module>.cobertura.xml.
# Run it inside the dev shell, from anywhere in the repository:
#   nix develop --command bash -c 'tools/coverage/run.sh /tmp/coverage Debug Release'
set -euo pipefail
out=${1:?usage: tools/coverage/run.sh <out-dir> [Debug] [Release]}
shift
configurations=("${@:-Debug}")
root=$(git rev-parse --show-toplevel)
cd "$root"
for configuration in "${configurations[@]}"; do
  for module in Core Simulation Physics Rendering Host; do
    results="$out/$configuration/$module"
    rm -rf "$results"
    mkdir -p "$results"
    echo "== $configuration $module"
    dotnet test --project "tests/Tertius.$module.Tests" -c "$configuration" \
      --coverage --coverage-output-format cobertura --coverage-output "$module.cobertura.xml" \
      --results-directory "$results" 2>&1 | grep -E 'failed:|succeeded:|error' | head -5 || true
  done
done
