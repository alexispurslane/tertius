#!/usr/bin/env python3
"""Tool code (tools/runpod/WORKBENCH.md, "Tests in the cloud"): the class filters of one shard of a built test
assembly, for a cloud GPU worker.

    python3 -I tools/runpod/shards.py <test assembly .dll> <shard count> <shard index>

It asks the built assembly for its test classes (`dotnet <dll> -list classes`), nested ones included as Outer+Inner,
and prints the runner's filters of shard <index> of <count>: `-class A -class B ...`. Each shard runs every
<count>-th class of the sorted list, so the shards of one count hold every class exactly once. A hand-written list
misses classes: the shard lists of 2026-11-12 and of one run on 2026-11-13 left out up to 19 classes, most of them
nested, because a filter on an outer class does not run its nested classes (ticket ter-eugk). The standard library
only.
"""

import subprocess
import sys


def classes_of(listing: str) -> list:
    """The test classes of the output of `-list classes`: each line that is one dotted name, without the banner."""
    names = []
    for line in listing.splitlines():
        name = line.strip()
        if name and " " not in name and "." in name:
            names.append(name)
    return names


def shard(classes: list, count: int, index: int) -> list:
    """Shard `index` of `count`: every `count`-th class of the sorted list, from the `index`-th."""
    return sorted(classes)[index::count]


def uncovered(classes: list, parts: list) -> list:
    """The classes that no part holds, in the order of `classes`."""
    held = {name for part in parts for name in part}
    return [name for name in classes if name not in held]


def filters(classes: list) -> list:
    """The runner's arguments that run exactly `classes`."""
    return [argument for name in classes for argument in ("-class", name)]


def main(argv: list) -> int:
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    assembly, count, index = argv[0], int(argv[1]), int(argv[2])
    if not 0 <= index < count:
        print(f"shards.py: the index {index} is not below the count {count}", file=sys.stderr)
        return 2
    listing = subprocess.run(["dotnet", assembly, "-list", "classes", "-noColor"], capture_output=True, text=True,
                             check=True).stdout
    classes = classes_of(listing)
    if not classes:
        print(f"shards.py: {assembly} lists no test class", file=sys.stderr)
        return 1
    missing = uncovered(classes, [shard(classes, count, k) for k in range(count)])
    if missing:
        print(f"shards.py: no shard holds {', '.join(missing)}", file=sys.stderr)
        return 1
    print(" ".join(filters(shard(classes, count, index))))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
