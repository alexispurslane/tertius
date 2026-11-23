#!/usr/bin/env bash
# Tool code (t1-gout, step 3). tilestills.sh <before bin> <after bin> <stills.py out> <run folder> <case>...: each still
# case of stills.py on two builds at one exposure. The before build runs first with the automatic exposure, and its
# exposure at the case's frame is fixed for both builds, at the render scales 2/3 and 0.96, 1920 x 1080, no bloom. Runs
# under the caller's shared GPU lock. Writes <run folder>/<case>/<build>-s<scale>.png and exposure.txt, and deletes
# every other frame.
set -u
before=$1; after=$2; out=$3; run=$4; shift 4
R=/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7
for c in "$@"; do
  read -r name script frame file <<< "$(awk -v c="$c" '$1 == c' "$out/cases.txt")"
  d=$run/$c; mkdir -p "$d"
  [ -e "$d/done" ] && continue
  go() { nix develop $R --command "$1" --script "$out/scripts/$script" --headless --out "$d/$2" --size 1920x1080 --bloom off "${@:3}" > "$d/$2.log" 2>&1; }
  go "$before" auto
  x=$(python3 -I -c "import json,sys; print([e['stops'] for e in json.load(open(sys.argv[1])) if e['capture'] == sys.argv[2]][0])" "$d/auto/exposure.json" "$file")
  echo "$x" > "$d/exposure.txt"
  for s in 0.6667 0.96; do
    for b in before after; do
      bin=$before; [ $b = after ] && bin=$after
      go "$bin" "$b-s$s" --render-scale $s --exposure "$x"
      /bin/cp -f "$d/$b-s$s/$file.png" "$d/$b-s$s.png"
      /bin/rm -rf "$d/$b-s$s"
    done
  done
  /bin/rm -rf "$d/auto"
  touch "$d/done"; echo "$(date +%T) $c exposure $x"
done
