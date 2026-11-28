# Diagnostic tool code. variant-cases.sh <worktree> <out root> <name:variant:gain[:NAME=value,...]>...
# For each case, sets RESTRAINT_VARIANT and COLOR_GAIN in the worktree's volume.glsl (the #42 prototype's switches; a
# local edit, never committed) and any further "#define NAME" of the case (such as the #42 ramp), builds the demo
# in Release and runs helmet-pair.sh into <out root>/<name>. A case whose two films are complete is skipped. The
# worktree needs tools/diagnostics.patch applied. Each case holds the shared GPU lock for two films, about 4 minutes.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../../.." && pwd)
wt=$(cd "$1" && pwd); root=$2; shift 2
glsl=$wt/src/Tertius.Rendering/Shaders/volume.glsl
for case in "$@"; do
  IFS=: read -r name variant gain extra <<< "$case"
  defines="RESTRAINT_VARIANT=$variant,COLOR_GAIN=$gain${extra:+,$extra}"
  IFS=, read -ra pairs <<< "$defines"
  for pair in "${pairs[@]}"; do
    key=${pair%%=*}; value=${pair#*=}
    grep -q "^#define $key " "$glsl" || { echo "no #define $key in $glsl" >&2; exit 1; }
    perl -pi -e "s/^#define $key .*/#define $key $value/" "$glsl"
    grep -q "^#define $key $value\$" "$glsl"
  done
  if [ -f "$root/$name/black/exposure.json" ] && [ -f "$root/$name/normal/exposure.json" ]; then
    echo "case $name: complete, skipped"; continue
  fi
  start=$(date +%s)
  (cd "$wt" && nix develop "$repo" --command bash -c 'dotnet build src/Tertius.Demo -c Release -v quiet -nologo -clp:ErrorsOnly') > "$root-$name-build.log" 2>&1
  mkdir -p "$root/$name"
  bash "$here/helmet-pair.sh" "$wt" "$root/$name" > "$root/$name/measure.txt" 2>&1
  echo "case $name ($defines): $(( $(date +%s) - start )) s"
done
