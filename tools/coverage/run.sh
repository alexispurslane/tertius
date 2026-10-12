#!/usr/bin/env bash
# Tool code. Runs every test project with code coverage, in each build configuration given, and writes one Cobertura
# report per project to <out>/<configuration>/<module>/<module>.cobertura.xml, with the test output in test.log there.
# Run it inside the dev shell, from anywhere in the repository:
#   nix develop --command bash -c 'bash tools/coverage/run.sh /tmp/coverage Debug Release'
set -euo pipefail
# macOS removes the DYLD_* variables when it starts a protected program such as /usr/bin/env, which the #! line runs.
# Without DYLD_FALLBACK_LIBRARY_PATH, the renderer cannot find libvulkan, and every GPU test fails. The dev shell's
# bash keeps the variables, so start the script with bash.
if [[ "$(uname)" == Darwin && -z "${DYLD_FALLBACK_LIBRARY_PATH:-}" ]]; then
  echo "DYLD_FALLBACK_LIBRARY_PATH is empty. Start the script with bash inside the dev shell: bash $0 ..." >&2
  exit 1
fi
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
    # The whole output goes to test.log beside the report. The terminal gets the counts and the first failed tests.
    dotnet test --project "tests/Tertius.$module.Tests" -c "$configuration" \
      --coverage --coverage-output-format cobertura --coverage-output "$module.cobertura.xml" \
      --results-directory "$results" > "$results/test.log" 2>&1 || true
    grep -E 'failed:|succeeded:|error CS|failed Tertius' "$results/test.log" | head -8 || true
  done
done
