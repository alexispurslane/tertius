# render.sh <worktree> <outdir> [env assignments...]
set -eu
wt=$1; out=$2; shift 2
mkdir -p "$out"
export TERTIUS_CONTENT_ROOT=/Users/alexispurslane/Development/tertius/content
S=/private/tmp/claude-501/-Users-alexispurslane--t3-worktrees-tertius-t3-7d632f6e-af62e861215c5d93/dd29e80b-7410-49bd-bb44-d1ad2cae6cdd/scratchpad
env "$@" bash /Users/alexispurslane/.t3/worktrees/tertius/t3-7d632f6e-af62e861215c5d93/tools/gpu-lock.sh --shared "$out/lock.log" \
  "$wt/src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo" --script "${SCRIPT:-$S/cockpit.json}" --headless --out "$out" --exposure 6.0043726 > "$out/render.log" 2>&1
echo "done $out"
