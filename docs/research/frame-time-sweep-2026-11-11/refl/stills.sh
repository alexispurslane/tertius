#!/usr/bin/env bash
# Untimed work for the shared mode of the GPU lock, second round: the stills of the reflections scene for each prototype
# build at the fixed exposure of the first round, each with a film of 30 draws after its still of 33 draws, the long
# reference with an own ray for every pixel, and a still of the deck for two builds. Dev shell.
set -uo pipefail
S=/private/tmp/tertius-sweep-20261111
W=/Users/alexispurslane/.t3/worktrees/tertius/t3-26b378a0
R=$S/refl
cd $W
run() { # run <build> <script> <out name>
  local out=$R/out2/$3
  [ -f $out/done ] && return
  rm -rf $out; mkdir -p $out
  $S/bin-$1/Tertius.Demo --script $R/$2 --headless --out $out > $out.log 2>&1; local code=$?
  [ $code = 0 ] && touch $out/done
  echo "$3: $(ls $out | wc -l) files, exit $code"
}
mkdir -p $R/out2
export SWEEP_EXPOSURE=-7.2156887
for v in base ssr1 ssr4h ssr9h ownall halfup; do [ -x $S/bin-$v/Tertius.Demo ] && run $v views2.json $v; done
export SWEEP_EXPOSURE=0.7041458
for v in base ssr4h ssr1; do [ -x $S/bin-$v/Tertius.Demo ] && run $v deck.json deck-$v; done
export SWEEP_EXPOSURE=-7.2156887
run ownall reference2.json reference
