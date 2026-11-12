"""Compares the files of pairs of capture folders byte for byte.

usage: framecmp.py <out3 folder> <suffix a> <suffix b> [prefix]

Every folder <prefix><name>-<suffix a> pairs with <prefix><name>-<suffix b>. A row gives the name, the count of PNG
files, the count of PNG files that differ, and the same two counts for the JSON dumps of the entities. The timing and
exposure records are left out, because the timings differ from run to run.
"""
import filecmp
import os
import sys

root, a, b = sys.argv[1], sys.argv[2], sys.argv[3]
prefix = sys.argv[4] if len(sys.argv) > 4 else "drv-"
SKIP = {"gpu-timings.json", "done"}
total = [0, 0, 0, 0]
print("| Script | PNG files | PNG files that differ | Other files | Other files that differ |")
print("|---|---:|---:|---:|---:|")
for folder in sorted(os.listdir(root)):
    if not (folder.startswith(prefix) and folder.endswith("-" + a)) or not os.path.isdir(os.path.join(root, folder)):
        continue
    name = folder[len(prefix) : -len(a) - 1]
    da, db = os.path.join(root, folder), os.path.join(root, prefix + name + "-" + b)
    if not os.path.isdir(db):
        print(f"| `{name}` | no folder `{b}` | | | |")
        continue
    done = os.path.exists(os.path.join(da, "done")) and os.path.exists(os.path.join(db, "done"))
    files = sorted(set(os.listdir(da)) | set(os.listdir(db)))
    row = [0, 0, 0, 0]
    for f in files:
        if f in SKIP:
            continue
        kind = 0 if f.endswith(".png") else 2
        row[kind] += 1
        pa, pb = os.path.join(da, f), os.path.join(db, f)
        if not (os.path.exists(pa) and os.path.exists(pb) and filecmp.cmp(pa, pb, shallow=False)):
            row[kind + 1] += 1
    for i in range(4):
        total[i] += row[i]
    note = "" if done else " (a run did not end)"
    print(f"| `{name}`{note} | {row[0]} | {row[1]} | {row[2]} | {row[3]} |")
print(f"| Sum | {total[0]} | {total[1]} | {total[2]} | {total[3]} |")
