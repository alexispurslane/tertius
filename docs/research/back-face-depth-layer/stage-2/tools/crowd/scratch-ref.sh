#!/usr/bin/env bash
# Throwaway (t1-kiaw): a scratch commit of the crowd probe on the cards tree of stage 2 (66dae25e), from the exported tree.
# Run from the worktree: bash scratch-ref.sh <exported tree> <ref name> <message>
set -euo pipefail
src=$1; ref=$2; message=$3
base=$(git rev-parse refs/scratch/ter-kh4g-2-cards-fix)
index=$(mktemp /tmp/t1-kiaw-index.XXXXXX)
GIT_INDEX_FILE=$index git read-tree "$base"
GIT_INDEX_FILE=$index git --work-tree="$src" add -A -- src tools scripts
GIT_INDEX_FILE=$index git diff --cached --stat "$base"
tree=$(GIT_INDEX_FILE=$index git write-tree)
commit=$(git commit-tree "$tree" -p "$base" -m "$message")
git update-ref "$ref" "$commit"
rm -f "$index"
echo "$ref $commit"
