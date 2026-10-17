#!/usr/bin/env bash
# Tool code. Runs every test project with code coverage, in each build configuration given, and writes one Cobertura
# report per project to <out>/<configuration>/<module>/<module>.cobertura.xml, with the test output in test.log there.
# Run it inside the dev shell, from anywhere in the repository:
#   nix develop --command bash -c 'bash tools/coverage/run.sh /tmp/coverage Debug Release'
set -euo pipefail
# The build copies the Jolt library and the Vulkan loader next to each output from the dev shell's variables, so
# the tests need no DYLD_* variable. Without the dev shell, the GPU tests cannot find libvulkan.
if [[ -z "${TERTIUS_VULKAN_DIR:-}" ]]; then
  echo "TERTIUS_VULKAN_DIR is empty. Run the script inside the dev shell: nix develop --command bash $0 ..." >&2
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
