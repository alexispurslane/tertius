#!/usr/bin/env bash
# The frame runs of the patched MoltenVK's acceptance and of the step cap's image test: one shared take of the GPU lock
# for each named part. Each take runs refl/stills4.sh with the part's own job list, refl/stills-wide-<part>.jobs. Run in
# the dev shell of the ticket's branch.
set -u
S=/private/tmp/tertius-sweep-20261111
L=/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh
for part in "$@"; do
  bash $L --shared $S/stills-wide-$part.lock.log bash $S/refl/stills4.sh $S/refl/stills-wide-$part.jobs > /dev/null 2>&1
  echo "wide $part: exit $?"
done
