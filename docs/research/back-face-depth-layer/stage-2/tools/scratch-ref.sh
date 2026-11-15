#!/usr/bin/env bash
# Throwaway (t1-yy2f): a scratch commit of the stage 2 probe on the census tree of stage 1b, from the exported tree.
# Run from the worktree: bash scratch-ref.sh <exported tree> <ref name> <message>
# The commit holds every file of the export that differs from the census tree, without bin and obj folders.
set -euo pipefail
src=$1; ref=$2; message=$3
base=$(git rev-parse refs/scratch/ter-kh4g-1b-sill-census-ahead)
index=$(mktemp /tmp/t1-yy2f-index.XXXXXX)
GIT_INDEX_FILE=$index git read-tree "$base"
GIT_INDEX_FILE=$index git --work-tree="$src" add -A -- src tools
GIT_INDEX_FILE=$index git diff --cached --stat "$base" | tail -1
tree=$(GIT_INDEX_FILE=$index git write-tree)
commit=$(git commit-tree "$tree" -p "$base" -m "$message")
git update-ref "$ref" "$commit"
rm -f "$index"
echo "$ref $commit"
