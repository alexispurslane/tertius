# Tool code of t0-5tyj. patchfiles.py <patch> <keep|drop> <file>...: prints the parts of a git patch whose file is (keep)
# or is not (drop) among the files, so that one file's hunks can come from another patch.
import re
import sys

path, mode, files = sys.argv[1], sys.argv[2], set(sys.argv[3:])
parts = re.split(r'(?m)^(?=diff --git )', open(path).read())
for part in parts:
    m = re.match(r'diff --git a/(\S+)', part)
    if m is None:
        if mode == 'drop':
            sys.stdout.write(part)
        continue
    if (m.group(1) in files) == (mode == 'keep'):
        sys.stdout.write(part)
