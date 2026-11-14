#!/usr/bin/env python3
"""fliplist.py <out folder> <prefix> <control suffix> <variant suffix> [--films]: the list of PNG pairs for flipcmp, one
"<script>/<capture>\t<control png>\t<variant png>" line per capture of each folder <prefix><script>-<control suffix>.
Without --films, the frames of a film (names that end in -NNNNNN) are left out."""
import glob, os, re, sys
out, prefix, ctl, var = sys.argv[1:5]
films = '--films' in sys.argv
for folder in sorted(glob.glob(f'{out}/{prefix}*-{ctl}')):
    script = os.path.basename(folder)[len(prefix):-len(ctl) - 1]
    for png in sorted(glob.glob(f'{folder}/*.png')):
        name = os.path.basename(png)[:-4]
        if re.search(r'-\d{6}$', name) and not films:
            continue
        twin = f'{out}/{prefix}{script}-{var}/{name}.png'
        if os.path.exists(twin):
            print(f'{script}/{name}\t{png}\t{twin}')
