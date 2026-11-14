#!/usr/bin/env bash
# Throwaway (t1-n8x9): one scratch commit per probe mode, on the review base, with the probe's source and the mode as
# BackFacePass.DefaultMode. Run from the worktree while src holds the probe: bash scratch-refs.sh <base>
set -euo pipefail
base=$1
index=$(mktemp /tmp/t1-n8x9-index.XXXXXX)
work=$(mktemp -d /tmp/t1-n8x9-src.XXXXXX)
files=$(git diff --name-only "$base" -- src)
for mode in off ten trim layer layer/plane layer/ten layer/solid layer+fade; do
  GIT_INDEX_FILE=$index git read-tree "$base"
  for f in $files; do
    mkdir -p "$work/$(dirname "$f")"
    if [[ $f == */BackFacePass.cs ]]; then
      sed "s|private const string DefaultMode = \"off\";|private const string DefaultMode = \"$mode\";|" "$f" > "$work/$f"
    else
      cp "$f" "$work/$f"
    fi
    blob=$(git hash-object -w "$work/$f")
    GIT_INDEX_FILE=$index git update-index --add --cacheinfo 100644 "$blob" "$f"
  done
  tree=$(GIT_INDEX_FILE=$index git write-tree)
  name=$(echo "$mode" | tr '/+' '--')
  commit=$(git commit-tree "$tree" -p "$base" -m "THROWAWAY probe t1-n8x9: the slab in the depth pyramid, default mode $mode")
  git update-ref "refs/scratch/ter-kh4g-1b-$name" "$commit"
  echo "refs/scratch/ter-kh4g-1b-$name $commit $mode"
done
rm -rf "$index" "$work"
