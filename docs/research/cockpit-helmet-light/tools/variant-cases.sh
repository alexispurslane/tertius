# Diagnostic tool code. variant-cases.sh <worktree> <out root> <name:variant:gain>...
# For each case, sets RESTRAINT_VARIANT and COLOR_GAIN in the worktree's volume.glsl (the #42 prototype's switches; a
# local edit, never committed), builds the demo in Release and runs helmet-pair.sh into <out root>/<name>. The worktree
# needs tools/diagnostics.patch applied. Each case holds the shared GPU lock for two films, about 4 minutes.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../../.." && pwd)
wt=$(cd "$1" && pwd); root=$2; shift 2
glsl=$wt/src/Tertius.Rendering/Shaders/volume.glsl
for case in "$@"; do
  IFS=: read -r name variant gain <<< "$case"
  perl -pi -e "s/^#define RESTRAINT_VARIANT .*/#define RESTRAINT_VARIANT $variant/; s/^#define COLOR_GAIN .*/#define COLOR_GAIN $gain/" "$glsl"
  grep -q "^#define RESTRAINT_VARIANT $variant\$" "$glsl" && grep -q "^#define COLOR_GAIN $gain\$" "$glsl"
  start=$(date +%s)
  (cd "$wt" && nix develop "$repo" --command bash -c 'dotnet build src/Tertius.Demo -c Release -v quiet -nologo -clp:ErrorsOnly') > "$root-$name-build.log" 2>&1
  mkdir -p "$root/$name"
  bash "$here/helmet-pair.sh" "$wt" "$root/$name" > "$root/$name/measure.txt" 2>&1
  echo "case $name (variant $variant, gain $gain): $(( $(date +%s) - start )) s"
done
