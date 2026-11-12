#!/usr/bin/env bash
# The acceptance test runs of the patched MoltenVK (ter-2zga), one shared take of the GPU lock each. Run in the dev
# shell of the ticket's branch: the lock helper needs the shell's bash. The Rendering tests under Metal's API validation
# run in three parts by the first letter of the test class, so that no take holds the lock for long. The third part
# takes every class that the first two leave out.
set -u
S=/private/tmp/tertius-sweep-20261111
L=/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh
for name in "$@"; do
  bash $L --shared $S/acc-$name.lock.log bash $S/acc-$name.cmd.sh > $S/acc-$name.out 2>&1
  echo "$name: exit $?"
done
