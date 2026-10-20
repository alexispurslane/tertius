#!/usr/bin/env bash
# Tool code. Builds the solution once per build configuration, then runs every test project in it. It prints one line
# per configuration and module with the counts and the names of the failed tests, and writes each module's full output
# to <out>/<configuration>/<module>.log. Run it inside the dev shell, from anywhere in the repository:
#   nix develop --command bash -c 'bash tools/test-all.sh'
#   nix develop --command bash -c 'bash tools/test-all.sh -c Debug -m Core,Physics -o /tmp/tertius-tests -- --filter-class "*FrameTree*"'
# Options:
#   -c <list>  build configurations, separated by commas (default Debug,Release)
#   -m <list>  test modules, separated by commas (default Core,Simulation,Physics,Rendering,Host)
#   -o <dir>   folder for the logs (default a new temporary folder)
#   -- <args>  everything after -- goes to each dotnet test command unchanged, for example a filter
# The exit code is 1 if a build failed, a test failed or a test run ended without its summary, and 0 otherwise. It is 2 for a wrong option or a module with no test project.
# A module where no test matches the filter is not a failure. Its line says "no tests ran".
set -euo pipefail
# The build copies the Jolt library and the Vulkan loader next to each output from the dev shell's variables, so
# the tests need no DYLD_* variable. Without the dev shell, the GPU tests cannot find libvulkan.
if [[ -z "${TERTIUS_VULKAN_DIR:-}" ]]; then
  echo "TERTIUS_VULKAN_DIR is empty. Run the script inside the dev shell: nix develop --command bash $0 ..." >&2
  exit 1
fi
usage="usage: tools/test-all.sh [-c Debug,Release] [-m Core,Simulation,Physics,Rendering,Host] [-o <out-dir>] [-- <dotnet test args>]"
configurations=Debug,Release
modules=Core,Simulation,Physics,Rendering,Host
out=
while getopts "c:m:o:h" option; do
  case $option in
    c) configurations=$OPTARG ;;
    m) modules=$OPTARG ;;
    o) out=$OPTARG ;;
    *) echo "$usage" >&2; exit 2 ;;
  esac
done
shift $((OPTIND - 1))
passthrough=("$@")
IFS=, read -r -a configurations <<< "$configurations"
IFS=, read -r -a modules <<< "$modules"
root=$(git rev-parse --show-toplevel)
cd "$root"
for module in "${modules[@]}"; do
  if [[ ! -d "tests/Tertius.$module.Tests" ]]; then
    echo "No test project tests/Tertius.$module.Tests. $usage" >&2
    exit 2
  fi
done
out=${out:-$(mktemp -d "${TMPDIR:-/tmp}/tertius-tests.XXXXXX")}
mkdir -p "$out"
out=$(cd "$out" && pwd)
echo "Logs: $out"
status=0
for configuration in "${configurations[@]}"; do
  rm -rf "${out:?}/$configuration"
  mkdir -p "$out/$configuration"
  # One build per configuration. The test runs then use --no-build, so they do not build the shared projects again.
  if ! dotnet build Tertius.slnx -c "$configuration" > "$out/$configuration/build.log" 2>&1; then
    echo "$configuration build FAILED, see $out/$configuration/build.log"
    grep -E ': error [A-Za-z]+[0-9]+:' "$out/$configuration/build.log" | sort -u | head -5 || true
    status=1
    continue
  fi
  for module in "${modules[@]}"; do
    log="$out/$configuration/$module.log"
    code=0
    dotnet test --project "tests/Tertius.$module.Tests" -c "$configuration" --no-build \
      ${passthrough[@]+"${passthrough[@]}"} > "$log" 2>&1 || code=$?
    # The runner ends with a summary of lines such as "  total: 12". Exit code 8 means that no test ran.
    count() { sed -n "s/^  $1: \([0-9]*\)$/\1/p" "$log" | tail -1; }
    total=$(count total) failed=$(count failed) succeeded=$(count succeeded) skipped=$(count skipped)
    if [[ -z "$total" ]]; then
      echo "$configuration $module: no summary, exit code $code, see $log"
      status=1
      continue
    fi
    line="$configuration $module: total $total, failed $failed, succeeded $succeeded, skipped $skipped"
    if [[ $code -eq 8 && $total -eq 0 ]]; then
      line="$line (no tests ran)"
    elif [[ $code -ne 0 || $failed -ne 0 ]]; then
      status=1
      line="$line, exit code $code"
    fi
    echo "$line"
    # A failed test prints "failed <name> (<duration>)" at the start of a line.
    grep -E '^failed ' "$log" | sed -E 's/^failed //; s/ \([^()]*\)$//' | sed 's/^/    failed: /' || true
  done
done
exit $status
