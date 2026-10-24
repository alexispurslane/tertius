#!/usr/bin/env bash
# Formats and modernizes C# files in two passes:
#   1. dotnet format style applies Roslyn's modernization fixes: the IDE rules that .editorconfig sets to suggestion, and
#      the ones that it sets to warning (IDE0063, and the rules against unused code, IDE0051, IDE0052 and IDE0060). The
#      build fails at a hit of a warning rule, so on code that builds those rules find nothing to fix.
#   2. jb cleanupcode runs ReSharper's cleanup with the Tertius profile (Tertius.slnx.DotSettings): syntax style,
#      redundancies, member layout and formatting. It runs last, so its formatting wins.
#
# Usage:
#   tools/cleanup.sh               the C# files that differ from main, committed or not
#   tools/cleanup.sh --all         every C# file in Tertius.slnx (about 10 minutes)
#   tools/cleanup.sh <file>...     the given C# files
#
# Run it in the dev shell. The pre-push hook runs it on the files that a push changes (tools/README.md, "Git hooks").
set -euo pipefail

root=$(git rev-parse --show-toplevel)
cd "$root"

files=()
all=false
if [[ $# -eq 0 ]]; then
    base=$(git merge-base HEAD main)
    while IFS= read -r f; do files+=("$f"); done < <(git diff --name-only --diff-filter=ACMR "$base" -- '*.cs')
elif [[ "$1" == "--all" ]]; then
    all=true
else
    files=("$@")
fi
if ! $all && [[ ${#files[@]} -eq 0 ]]; then
    echo "cleanup: no C# files to clean." >&2
    exit 0
fi

ids=$(grep -oE '^dotnet_diagnostic\.IDE[0-9]+\.severity = (suggestion|warning)' .editorconfig | sed -E 's/^dotnet_diagnostic\.(IDE[0-9]+).*/\1/')

project_of() {
    local dir
    dir=$(dirname "$1")
    while [[ "$dir" != "." && "$dir" != "/" ]]; do
        for p in "$dir"/*.csproj; do
            [[ -f "$p" ]] && { echo "$p"; return; }
        done
        dir=$(dirname "$dir")
    done
}

# Both tools load a solution. ReSharper cannot clean files with a project as its target, and it ignores an absolute
# --include path. So for a list of files, the script writes a solution with only their projects at the repository
# root, and every path is relative to the root.
if $all; then
    solution=Tertius.slnx
    include_args=()
else
    declare -A projects=()
    kept=()
    for f in "${files[@]}"; do
        p=$(project_of "$f")
        if [[ -n "$p" ]]; then
            projects["$p"]=1
            kept+=("$f")
        else
            echo "cleanup: $f belongs to no project, so the cleanup skips it." >&2
        fi
    done
    [[ ${#kept[@]} -eq 0 ]] && exit 0
    files=("${kept[@]}")
    solution=.cleanup.slnx
    trap 'rm -f "$root/$solution"' EXIT
    {
        echo "<Solution>"
        for p in "${!projects[@]}"; do echo "  <Project Path=\"$p\" />"; done
        echo "</Solution>"
    } > "$solution"
    include_args=(--include "${files[@]}")
fi

what=$($all && echo "every C# file" || echo "${#files[@]} C# file(s)")
echo "cleanup: Roslyn modernization fixes on $what..." >&2
# shellcheck disable=SC2086 # the rule IDs are separate words on purpose
dotnet format style "$solution" --severity info --diagnostics $ids "${include_args[@]}" --verbosity quiet

echo "cleanup: ReSharper cleanup on $what..." >&2
dotnet tool restore > /dev/null
jb_args=(--settings=Tertius.slnx.DotSettings --profile=Tertius --verbosity=ERROR)
if ! $all; then
    jb_args+=("--include=$(IFS=';'; echo "${files[*]}")")
fi
log=$(mktemp -t tertius-cleanup.XXXXXX)
if ! dotnet jb cleanupcode "$solution" "${jb_args[@]}" > "$log" 2>&1; then
    # ReSharper stops on a compile error in the projects that it loads.
    grep -oE '[^ ]+\.cs \([0-9]+,[0-9]+\)[^ ]* +CS[0-9]{4}: .*' "$log" | sort -u | head -40 >&2 || true
    echo "cleanup: jb cleanupcode failed. Its full log: $log" >&2
    exit 1
fi
# A path that ReSharper does not match makes it clean nothing and still succeed.
if grep -q "No items were found to cleanup" "$log"; then
    echo "cleanup: jb cleanupcode matched none of the files, which is a bug in this script. Its log: $log" >&2
    exit 1
fi
rm -f "$log"
