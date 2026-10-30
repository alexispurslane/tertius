"""Prints what the staged changes touch in the code: the long methods, the erosion and the verbosity.

The pre-commit hook runs it after the slop metrics of the staged tree end, which is after the tests. It never fails the
commit: it is a report.

- Length: every compile writes its diagnostics to obj/<configuration>/<framework>/analyzers.sarif
  (Directory.Build.targets), and the pre-commit hook builds Checked. The script reads MA0051 (method length) from the
  projects of the staged files and prints each one whose method holds a changed line, in src and in tests.
- Erosion, verbosity and their absolute counts: with --slop <file>, the script reads the report that `Tertius.Slop measure --details <file>`
  wrote for the staged tree (tools/README.md, "Slop metrics"). It prints each metric of src, with its change against the
  baseline of the details: the note of the nearest noted commit up to HEAD on the first-parent line, by the rule of the
  push summary. That is the parent of a new commit, or an older commit when the parent has no note. In a
  `git commit --amend`, HEAD is the commit that the amend replaces. Under the erosion, it names each function with a CC
  above the limit that holds a changed line. Under the verbosity, it names each finding of a rule and each clone that
  holds a changed line. Each metric prints even when the commit changes no C# file. Without --slop, as when the
  measure failed or a test failed, both sections are left out.

The slop metrics measure only src, because test code repeats its setup on purpose, so that each test reads on its own,
and a test's branches are its checks. So tests/ gets the length list only.

When the report names anything, it ends with a prompt to refactor what the commit touches. The code improves a little
at each change that passes through it, not all at once and not never.

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


def metric_line(name, metric, value, baseline, digits=4):
    """The value of a metric of the staged tree, with its change against the baseline's note when there is one."""
    if baseline is None or metric not in baseline["note"]:
        return f"{name} {value:.{digits}f}"
    # Rounded first, so that a change below the last shown digit prints as +0.0000, not as -0.0000.
    change = round(value - baseline["note"][metric], digits) + 0.0
    return f"{name} {value:.{digits}f} ({change:+.{digits}f} against {baseline['commit']}, the nearest noted commit up to HEAD)"


def place(item):
    """file:line, or file:first-last for an item of more than one line."""
    lines = str(item["line"]) if item["line"] == item["endLine"] else f"{item['line']}-{item['endLine']}"
    return f"{item['file']}:{lines}"


def changes(changed, item):
    return touches(changed.get(item["file"], ()), item["line"], item["endLine"])


def report_erosion(changed, erosion, baseline):
    """Prints the erosion and the functions above the CC limit that hold a changed line. True if it named any."""
    print(metric_line("Erosion", "erosion", erosion["erosion"], baseline))
    print(metric_line("High CC mass", "highCcMass", erosion["highCcMass"], baseline, digits=1))
    print(metric_line("High CC functions", "highCcFunctionCount", erosion["highCcFunctionCount"], baseline, digits=0))
    limit = erosion["complexityLimit"]
    functions = [f for f in erosion["highCcFunctions"] if changes(changed, f)]
    if not functions:
        return False
    print(f"  {len(functions)} changed function(s) have a CC above {limit}, the greatest mass first:")
    for f in functions:
        print(f"    {f['file']}:{f['line']} {f['name']}: CC {f['cc']}, SLOC {f['sloc']}, mass {f['mass']:.1f}")
    print(f"  A split that brings such a function to CC {limit} or less lowers the erosion.")
    return True


def report_verbosity(changed, verbosity, baseline):
    """Prints the verbosity, and the findings and clones that hold a changed line. True if it named any."""
    print(metric_line("Verbosity", "verbosity", verbosity["verbosity"], baseline))
    print(metric_line("Verbose lines", "verboseLines", verbosity["verboseLines"], baseline, digits=0))
    messages = {rule["id"]: rule["message"] for rule in verbosity["rules"]}
    findings = [f for f in verbosity["findings"] if changes(changed, f)]
    clones = [c for c in verbosity["clones"] if changes(changed, c["first"]) or changes(changed, c["second"])]
    if findings:
        print(f"  {len(findings)} finding(s) of the verbosity rules cover changed lines:")
        for f in findings:
            print(f"    {place(f)} {f['rule']}: {messages[f['rule']]}")
    if clones:
        print(f"  {len(clones)} clone(s) hold changed lines:")
        for c in clones:
            print(f"    {place(c['first'])} and {place(c['second'])}")
    return bool(findings or clones)


def report_slop(changed, details):
    """Prints the erosion and the verbosity of the staged tree from the measure's details file. True if it named any."""
    details = json.loads(Path(details).read_text())
    report, baseline = details["report"], details["baseline"]
    named = report_erosion(changed, report["erosion"], baseline)
    return report_verbosity(changed, report["verbosity"], baseline) or named


def main():
    parser = argparse.ArgumentParser(description="Prints what the staged changes touch in the code.")
    parser.add_argument("--slop", metavar="FILE", help="the details that Tertius.Slop measure wrote for the staged tree")
    arguments = parser.parse_args()
    changed = changed_lines()
    reports = [report_length]
    if arguments.slop:
        reports.append(lambda lines: report_slop(lines, arguments.slop))
    reported = False
    for report in reports:
        try:
            reported = report(changed) or reported
        except Exception as error:  # a report never stops the commit
            print(f"the commit report failed: {error}", file=sys.stderr)
    if reported:
        print(REFACTOR_PROMPT)


if __name__ == "__main__":
    main()
