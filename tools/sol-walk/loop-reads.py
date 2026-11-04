"""Tool code of the spike ter-sftw, never merged. Counts the accesses of one buffer in an MSL file that sit inside a loop
(docs/research/temporal-renderer/profiling-spike.md, "The record's reads"). A loop is the body of a `for (;;)` or a
`while`/`do` that SPIRV-Cross writes, from its opening brace to the matching closing brace, except a `do` that ends in
`while(false)`.

Usage: python3 -I tools/sol-walk/loop-reads.py <file.metal> <regex of the buffer's access>

The regex names the buffer as the MSL writes it, for example `m_185\\)\\._m0\\[` for MoltenVK's dump or `_185\\._m0\\[` for
spirv-cross's command line. Prints each access with its line number and whether it is inside a loop, then the counts.
"""

import re
import sys
from pathlib import Path

LOOP = re.compile(r"^\s*(for \(;;\)|while \(|do$|do\s*\{)")
ONCE = re.compile(r"^\s*\} while\s*\(false\);")


def loop_lines(lines: list[str]) -> set[int]:
    """The numbers of the lines inside a loop's braces, 0-based. A `do { ... } while(false);` runs once: SPIRV-Cross
    writes it so that a `break` can leave a block, so it is no loop."""
    inside: set[int] = set()
    for start, line in enumerate(lines):
        if not LOOP.match(line):
            continue
        depth = 0
        opened = False
        body: list[int] = []
        for number in range(start, len(lines)):
            depth += lines[number].count("{") - lines[number].count("}")
            opened = opened or "{" in lines[number]
            if opened:
                body.append(number)
            if opened and depth <= 0:
                break
        if not ONCE.match(lines[body[-1]]):
            inside.update(body)
    return inside


def main() -> None:
    lines = Path(sys.argv[1]).read_text().splitlines()
    access = re.compile(sys.argv[2])
    inside = loop_lines(lines)
    in_loop = total = 0
    for number, line in enumerate(lines):
        if not access.search(line):
            continue
        total += 1
        where = "loop" if number in inside else "    "
        in_loop += number in inside
        print(f"{number + 1:6} {where} {line.strip()[:120]}")
    print(f"{total} accesses, {in_loop} inside a loop")


if __name__ == "__main__":
    main()
