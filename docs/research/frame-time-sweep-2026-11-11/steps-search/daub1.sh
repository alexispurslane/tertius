#!/usr/bin/env bash
# The daub stills of the first step cap screen: main's caps, a reflection cap of 96 and a diffuse cap of 48.
S=/private/tmp/tertius-sweep-20261111
bash $S/daub-stills.sh ctl
bash $S/daub-stills.sh r96 SWEEP_RSTEPS=96
bash $S/daub-stills.sh d48 SWEEP_DSTEPS=48
