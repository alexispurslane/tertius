# Tool code: shared by the coverage and Stryker helpers. Reads the lines that a branch changed from git.
import os, re, subprocess
from collections import defaultdict


def repo_root():
    return subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], text=True).strip()


def changed_lines(root, base, pathspecs):
    """Maps each changed .cs file (relative to the root) to the set of its added or changed line numbers (1-based).

    The diff runs from the merge base of `base` and HEAD to HEAD, so commits on `base` after the branch point do not count.
    Deleted files and deletion-only hunks give no lines."""
    merge_base = subprocess.check_output(['git', '-C', root, 'merge-base', base, 'HEAD'], text=True).strip()
    diff = subprocess.check_output(['git', '-C', root, 'diff', '-U0', merge_base, 'HEAD', '--', *pathspecs], text=True)
    changed = defaultdict(set)
    current = None
    for line in diff.splitlines():
        if line.startswith('+++ '):
            current = None if line.endswith('/dev/null') or not line.endswith('.cs') else line[6:]
        elif line.startswith('@@') and current:
            m = re.search(r'\+(\d+)(?:,(\d+))?', line)
            start, count = int(m.group(1)), int(m.group(2) or 1)
            changed[current].update(range(start, start + count))
    return changed


def runs(numbers):
    """Groups sorted line numbers into (first, last) runs of consecutive lines."""
    out = []
    for n in sorted(numbers):
        if out and n == out[-1][1] + 1:
            out[-1] = (out[-1][0], n)
        else:
            out.append((n, n))
    return out
