"""Prints what the staged changes touch in the code: the long methods, the erosion and the verbosity.

The pre-commit hook runs it after the slop metrics of the staged tree end, which is after the tests. It never fails the
commit: it is a report.

- Length: every compile writes its diagnostics to obj/<configuration>/<framework>/analyzers.sarif
  (Directory.Build.targets), and the pre-commit hook builds Checked. The script reads MA0051 (method length) from the
  projects of the staged files and prints each one whose method holds a changed line, in src and in tests.
- With --slop <file>, the report lists touched functions above the complexity limit, verbosity-rule findings and
  clones from `Tertius.Slop measure --details <file>`. Findings do not depend on a change in a project ratio.
- After the findings and refactoring prompt, a compact addendum shows project erosion, verbosity, TCC and depth,
  with comparable ratio changes. It names the baseline once: the nearest noted commit up to HEAD on the first-parent
  line. In `git commit --amend`, HEAD is the commit that the amend replaces. Without --slop, the addendum is omitted.

The slop metrics measure only src, because test code repeats its setup on purpose, so that each test reads on its own,
and a test's branches are its checks. So tests/ gets the length list only.

When the report names anything, it prints a prompt to refactor what the commit touches, before the addendum.
The code improves a little at each change that passes through it, not all at once and not never.

A changed line is a line that the staged diff adds or edits. A deletion counts as a change of the line after it, so
that a deletion inside a function changes the function, and a deletion of the lines that follow a function leaves the
function unchanged.
"""
import argparse
import json
import re
import subprocess
import sys
from pathlib import Path
from urllib.parse import unquote, urlparse

ROOT = Path(subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True,
                           check=True).stdout.strip())
LENGTH_RULE = "MA0051"
REFACTOR_PROMPT = """\
Refactor now: this commit touches the code above, so improve it in this commit, as far as the task allows. Split a
long or complex method into named steps, and merge a duplicated block into one shared method. A flat switch with one
arm per case of a closed set is already its clearest form, so leave it. The code gets better a little at each change
that passes through it, not all at once and not never."""


def changed_lines():
    """Maps each staged C# file, relative to the root, to the set of its changed line numbers."""
    diff = subprocess.run(["git", "diff", "--cached", "-U0", "--diff-filter=ACMR", "--", "*.cs"],
                          capture_output=True, text=True, check=True, cwd=ROOT).stdout
    changed, current = {}, None
    for line in diff.splitlines():
        if line.startswith("+++ "):
            current = line[6:] if line.startswith("+++ b/") else None
            if current:
                changed[current] = set()
        elif line.startswith("@@") and current:
            start, _, count = re.match(r"@@ -\S+ \+(\d+)(,(\d+))? @@", line).groups()
            start, count = int(start), int(count) if count is not None else 1
            # A pure deletion has no new lines, and git names the line before it.
            changed[current].update(range(start, start + count) if count > 0 else [start + 1])
    return changed


def touches(lines, first, last):
    return any(first <= n <= last for n in lines)


def skip_literal(text, i):
    """Returns the index after the comment, string or character literal that starts at i, or i if none starts there."""
    if text.startswith("//", i):
        end = text.find("\n", i)
        return len(text) if end < 0 else end
    if text.startswith("/*", i):
        end = text.find("*/", i + 2)
        return len(text) if end < 0 else end + 2
    m = re.compile(r'[$@]*("{3,})').match(text, i)
    if m:  # a raw string literal ends at the same run of quotes
        end = text.find(m.group(1), m.end())
        return len(text) if end < 0 else end + len(m.group(1))
    m = re.compile(r'([$]*@[$]*|[$]*)"').match(text, i)
    if m:
        verbatim, j = "@" in m.group(1), m.end()
        while j < len(text):
            if verbatim and text.startswith('""', j):
                j += 2
            elif not verbatim and text[j] == "\\":
                j += 2
            elif text[j] == '"':
                return j + 1
            else:
                j += 1
        return j
    if text[i] == "'":
        m = re.compile(r"'(\\.|[^'\\])*'").match(text, i)
        if m:
            return m.end()
    return i


def method_end_line(text, start):
    """Returns the last line of the member whose name starts at offset start: its matching } or its ending ;."""
    depth, i = 0, start
    while i < len(text):
        after = skip_literal(text, i)
        if after != i:
            i = after
            continue
        c = text[i]
        if c in "([":
            depth += 1
        elif c in ")]":
            depth -= 1
        elif c == ";" and depth == 0:
            break
        elif c == "{" and depth == 0:
            braces = 0
            while i < len(text):
                after = skip_literal(text, i)
                if after != i:
                    i = after
                    continue
                braces += {"{": 1, "}": -1}.get(text[i], 0)
                if braces == 0:
                    break
                i += 1
            break
        i += 1
    return text.count("\n", 0, i) + 1


def report_length(changed):
    """Prints the methods over the length limit that hold a changed line. True if any."""
    projects = set()
    for f in changed:
        for parent in (ROOT / f).parents:
            if list(parent.glob("*.csproj")):
                projects.add(parent)
                break
    found = []
    for project in sorted(projects):
        for sarif in project.glob("obj/Checked/*/analyzers.sarif"):
            for result in json.loads(sarif.read_text())["runs"][0].get("results", []):
                if result["ruleId"] != LENGTH_RULE:
                    continue
                location = result["locations"][0]["physicalLocation"]
                path = Path(unquote(urlparse(location["artifactLocation"]["uri"]).path))
                name = str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) else None
                if name not in changed:
                    continue
                text = path.read_text()
                first = location["region"]["startLine"]
                lines = text.split("\n")
                offset = sum(len(line) + 1 for line in lines[:first - 1]) + location["region"]["startColumn"] - 1
                last = method_end_line(text, offset)
                if touches(changed[name], first, last):
                    found.append(f"  {name}:{first} length: {result['message']['text']}")
    if not found:
        return False
    print(f"Length: {len(set(found))} changed method(s) are over the limit:")
    print("\n".join(sorted(set(found))))
    return True


def metric_line(name, value, before=None):
    """A project ratio and its comparable change, with no invented value for missing data."""
    if value is None:
        return f"{name} n/a"
    line = f"{name} {value:.4f}"
    if before is not None:
        # Rounded first, so a change below the last shown digit does not print as -0.0000.
        change = round(value - before, 4) + 0.0
        line += f" ({change:+.4f})"
    return line


def place(item):
    """file:line, or file:first-last for an item of more than one line."""
    lines = str(item["line"]) if item["line"] == item["endLine"] else f"{item['line']}-{item['endLine']}"
    return f"{item['file']}:{lines}"


def changes(changed, item):
    return touches(changed.get(item["file"], ()), item["line"], item["endLine"])


def report_erosion(changed, erosion):
    """Prints touched functions above the CC limit. True if it named any."""
    limit = erosion["complexityLimit"]
    functions = [f for f in erosion["highCcFunctions"] if changes(changed, f)]
    if not functions:
        return False
    print(f"Complexity: {len(functions)} touched function(s) have CC above {limit}, greatest mass first:")
    for f in functions:
        print(f"    {f['file']}:{f['line']} {f['name']}: CC {f['cc']}, SLOC {f['sloc']}, mass {f['mass']:.1f}")
    print(f"  A split that brings such a function to CC {limit} or less lowers the erosion.")
    return True


def report_verbosity(changed, verbosity):
    """Prints touched rule findings and clone pairs. True if it named any."""
    messages = {rule["id"]: rule["message"] for rule in verbosity["rules"]}
    findings = [f for f in verbosity["findings"] if changes(changed, f)]
    clones = [c for c in verbosity["clones"] if changes(changed, c["first"]) or changes(changed, c["second"])]
    if findings:
        print(f"Verbosity: {len(findings)} rule finding(s) cover changed lines:")
        for f in findings:
            print(f"    {place(f)} {f['rule']}: {messages[f['rule']]}")
    if clones:
        print(f"Duplication: {len(clones)} clone pair(s) hold changed lines:")
        for c in clones:
            print(f"    {place(c['first'])} and {place(c['second'])}")
    return bool(findings or clones)


def report_classes(metrics, baseline):
    """Prints pooled TCC and depth ratios, then limitations on their interpretation."""
    if not metrics:
        print("  Type metrics unavailable (legacy report)")
        return
    now = metrics["summary"]
    before = (baseline or {}).get("note", {}).get("classMetrics")
    comparable = before and before.get("provenance") == now.get("provenance")
    for key, label in (("tcc", "TCC"), ("depth", "Depth")):
        count = now.get(key)
        if count is None:
            print(f"  {label} unavailable (legacy report)")
            continue
        previous = before.get(key) if comparable else None
        print("  " + metric_line(label, count.get("ratio"), previous.get("ratio") if previous else None))
    if now.get("bindingErrorCount", 0):
        print(f"  Binding errors: {now['bindingErrorCount']}; counts use available symbols (see details).")
    if before and not comparable:
        print("  No type metric deltas: configuration differs.")


def report_summary(details):
    """Prints the project ratios as an addendum, naming their common baseline once."""
    report, baseline = details["report"], details["baseline"]
    comparison = (f"changes against {baseline['commit']}, the nearest noted commit up to HEAD"
                  if baseline else "no baseline note")
    print(f"\nProject metrics ({comparison}):")
    before = baseline["note"] if baseline else {}
    for key, label in (("erosion", "Erosion"), ("verbosity", "Verbosity")):
        print("  " + metric_line(label, report[key][key], before.get(key)))
    report_classes(report.get("classMetrics"), baseline)


def main():
    parser = argparse.ArgumentParser(description="Prints what the staged changes touch in the code.")
    parser.add_argument("--slop", metavar="FILE", help="the details that Tertius.Slop measure wrote for the staged tree")
    arguments = parser.parse_args()
    changed = changed_lines()
    reports = [report_length]
    details = None
    if arguments.slop:
        try:
            details = json.loads(Path(arguments.slop).read_text())
            reports.extend((lambda lines: report_erosion(lines, details["report"]["erosion"]),
                            lambda lines: report_verbosity(lines, details["report"]["verbosity"])))
        except Exception as error:  # a failed measure does not suppress the length report
            print(f"the commit report failed: {error}", file=sys.stderr)
    reported = False
    for report in reports:
        try:
            reported = report(changed) or reported
        except Exception as error:  # a report never stops the commit
            print(f"the commit report failed: {error}", file=sys.stderr)
    if reported:
        print(REFACTOR_PROMPT)
    if details is not None:
        try:
            report_summary(details)
        except Exception as error:  # a summary failure does not suppress the findings or prompt
            print(f"the commit report failed: {error}", file=sys.stderr)


if __name__ == "__main__":
    main()
