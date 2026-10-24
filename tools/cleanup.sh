#!/usr/bin/env bash
# Cleans up C# files in two steps (tools/README.md, "Code cleanup"):
#   1. Roslyn makes every fix that changes the code but not its meaning. dotnet format style applies the IDE rules that
#      .editorconfig sets to suggestion or warning, and dotnet format analyzers applies the Roslynator rules (RCS) that
#      it sets the same way. The build fails at a hit of a warning rule, so on code that builds those rules find nothing
#      to fix.
#   2. CSharpier formats the files (.csharpierrc.yaml). It runs last, so its formatting wins. It skips the files in
#      .csharpierignore, whose C# 15 syntax it cannot parse. A file that it cannot parse stops the cleanup, and
#      CSharpier leaves that file as it was.
#
# Usage:
#   tools/cleanup.sh               the C# files that differ from main, committed or not
#   tools/cleanup.sh --all         every C# file in Tertius.slnx
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

rules() {
    grep -oE "^dotnet_diagnostic\.$1[0-9]+\.severity = (suggestion|warning)" .editorconfig |
        sed -E 's/^dotnet_diagnostic\.([A-Z]+[0-9]+).*/\1/'
}
ide=$(rules IDE)
rcs=$(rules RCS)

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

# dotnet format loads a solution. For a list of files, the script writes a solution with only their projects, which
# loads faster than the whole one.
if $all; then
    solution=Tertius.slnx
    include_args=()
    while IFS= read -r f; do files+=("$f"); done < <(git ls-files --cached --others --exclude-standard -- '*.cs')
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

# dotnet format loads the Debug build of the union generator and the style analyzer. Without the generator, the
# generated members are missing, and the fixes misread the code: IDE0004 took the cast in a.Equals((object)b) for
# redundant. An up-to-date build takes a few seconds.
echo "cleanup: building the union generator and the style analyzer in Debug..." >&2
for p in analyzers/Tertius.Unions analyzers/Tertius.Style; do
    dotnet build "$p" -c Debug -v quiet -nologo > /dev/null
done

# One fix can make room for another, such as an explicit type for new[] { ... } and then a collection expression. dotnet
# format applies each rule once, so the step repeats until it changes nothing, at most 3 times.
what=$($all && echo "every C# file" || echo "${#files[@]} C# file(s)")
before=$(cksum "${files[@]}")
for pass in 1 2 3; do
    echo "cleanup: Roslyn fixes on $what, pass $pass..." >&2
    # shellcheck disable=SC2086 # the rule IDs are separate words on purpose
    dotnet format style "$solution" --severity info --diagnostics $ide "${include_args[@]}" --verbosity quiet
    # shellcheck disable=SC2086
    dotnet format analyzers "$solution" --severity info --diagnostics $rcs "${include_args[@]}" --verbosity quiet
    after=$(cksum "${files[@]}")
    [[ "$after" == "$before" ]] && break
    before=$after
    [[ $pass -eq 3 ]] && echo "cleanup: the Roslyn fixes still changed code in pass 3. Two rules may fight." >&2
done

echo "cleanup: CSharpier on $what..." >&2
csharpier format --log-level Warning "${files[@]}"
