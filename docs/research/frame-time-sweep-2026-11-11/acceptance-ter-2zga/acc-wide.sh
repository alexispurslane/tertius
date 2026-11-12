#!/usr/bin/env bash
# The wide frame check of the patched MoltenVK on the merged branch (ter-2zga): one shared take of the GPU lock for each
# named part. Each take runs refl/stills3.sh with the part's job list. Run in the dev shell of the ticket's branch.
set -u
S=/private/tmp/tertius-sweep-20261111
L=/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh
for part in "$@"; do
  cp $S/refl/stills-wide-$part.jobs $S/refl/stills3.jobs
  bash $L --shared $S/stills-wide-$part.lock.log bash $S/refl/stills3.sh > /dev/null 2>&1
  echo "wide $part: exit $?"
done
