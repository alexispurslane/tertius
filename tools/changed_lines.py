# Tool code: shared by the coverage and Stryker helpers. Reads the lines that a branch changed from git.
import os, re, subprocess
from collections import defaultdict


def repo_root():
    return subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], text=True).strip()


def parse_diff(diff, suffixes=('.cs',)):
    """Reads a `git diff -U0` text. Returns two maps for the files that end in one of suffixes:
    - changed: each file (relative to the root) to the set of its added or changed line numbers (1-based);
    - removed: each file to the (line, count) of each hunk that removes more lines than it adds, where line is the
      first new line of the hunk, or the line after the deletion for a hunk that only deletes.
    Deleted files give nothing."""
    changed, removed = defaultdict(set), defaultdict(list)
    current = None
    for line in diff.splitlines():
        if line.startswith('+++ '):
            current = None if line.endswith('/dev/null') or not line.endswith(suffixes) else line[6:]
        elif line.startswith('@@') and current:
            m = re.match(r'@@ -\d+(?:,(\d+))? \+(\d+)(?:,(\d+))? @@', line)
            old, start, count = int(m.group(1) or 1), int(m.group(2)), int(m.group(3) or 1)
            changed[current].update(range(start, start + count))
            if old > count:
                removed[current].append((start if count else start + 1, old - count))
    return changed, removed


def _diff(root, base, pathspecs, head):
    """The diff from the merge base of `base` and `head` to `head`, so commits on `base` after the branch point do not
    count."""
    merge_base = subprocess.check_output(['git', '-C', root, 'merge-base', base, head], text=True).strip()
    return subprocess.check_output(['git', '-C', root, 'diff', '-U0', merge_base, head, '--', *pathspecs], text=True)


def changed_lines(root, base, pathspecs, head='HEAD'):
    """Maps each changed .cs file (relative to the root) to the set of its added or changed line numbers (1-based).
    Deleted files and deletion-only hunks give no lines."""
    return parse_diff(_diff(root, base, pathspecs, head))[0]


def changed_and_removed(root, base, pathspecs, head='HEAD', suffixes=('.cs',)):
    """The two maps of parse_diff for the files with suffixes on the branch from `base` to `head`."""
    return parse_diff(_diff(root, base, pathspecs, head), suffixes)


def runs(numbers):
    """Groups sorted line numbers into (first, last) runs of consecutive lines."""
    out = []
    for n in sorted(numbers):
        if out and n == out[-1][1] + 1:
            out[-1] = (out[-1][0], n)
        else:
            out.append((n, n))
    return out
