#!/usr/bin/env bash
# Prints two lines: the git tree hash of the code, then the absolute git folder of the worktree (tools/README.md,
# "Git hooks"). The code is the files under the paths in `code` below, tracked or untracked, without the files that
# .gitignore ignores. With no option, the script hashes the code in the working tree. With --staged, it hashes the
# staged code. It works on a copy of the index, so it never changes what is staged.
#
# The build of every test project writes the first form next to its output (Directory.Build.targets), and the test
# record extension copies it into the git folder after a full run passes. The pre-commit hook compares that record with
# the second form, so it skips the tests only when the commit holds exactly the code that the tests ran on.
set -euo pipefail

staged=false
[[ "${1:-}" == --staged ]] && staged=true

cd "$(git rev-parse --show-toplevel)"
git_dir=$(git rev-parse --absolute-git-dir)

# Everything that a build or a test reads. Tickets, docs and Markdown files are not code, so a ticket note or a docs
# edit does not make the tests run again. A new top-level path that the build reads must join this list.
code=(src tests analyzers tools benchmarks scripts native .config .editorconfig Directory.Build.props
    Directory.Build.targets Directory.Packages.props global.json Tertius.slnx flake.nix flake.lock)

index=$(mktemp "${TMPDIR:-/tmp}/tertius-code-tree.XXXXXX")
trap 'rm -f "$index"' EXIT
cp "$(git rev-parse --git-path index)" "$index"
export GIT_INDEX_FILE=$index

excludes=()
for path in "${code[@]}"; do excludes+=(":(exclude)$path"); done
# -f lets git rm remove a file whose staged content differs from both the disk and HEAD. It acts on the copy of the
# index, so it loses nothing.
git rm -r -f -q --cached --ignore-unmatch -- . "${excludes[@]}"

if ! $staged; then
    # git add stops at a path that neither the disk nor the index holds, so the list keeps only the paths that exist.
    present=()
    for path in "${code[@]}"; do
        if [[ -e "$path" ]] || git ls-files --error-unmatch -- "$path" > /dev/null 2>&1; then present+=("$path"); fi
    done
    git add -A -- "${present[@]}"
fi

git rm -r -f -q --cached --ignore-unmatch -- ':(glob)**/*.md'

git write-tree
echo "$git_dir"
