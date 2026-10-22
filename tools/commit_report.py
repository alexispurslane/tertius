"""Prints the duplicated blocks and the complex or long methods that the staged changes touch.

The pre-commit hook runs it after the build. It never fails the commit: it is a report.

- Duplicates: jscpd scans src/ and tests/. The script prints each clone with a copy that holds a changed line.
- Complexity and length: every compile writes its diagnostics to obj/Debug/<framework>/analyzers.sarif
  (Directory.Build.targets). The script reads CA1502 (cyclomatic complexity) and MA0051 (method length) from the
  projects of the staged files and prints each one whose method holds a changed line.

A changed line is a line that the staged diff adds or edits. A deletion counts as a change of the line after it.
"""
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from urllib.parse import unquote, urlparse

ROOT = Path(subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True,
                           check=True).stdout.strip())
RULES = {"CA1502": "complexity", "MA0051": "length"}


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
            changed[current].update(range(start, start + max(count, 1)))
    return changed


def touches(lines, first, last):
    return any(first <= n <= last for n in lines)


def project_relative(path):
    # jscpd names each file relative to the folder that it scanned, so src/Tertius.Core/X.cs becomes Tertius.Core/X.cs.
    parts = Path(path).parts
    return str(Path(*parts[1:])) if parts and parts[0] in ("src", "tests") else path


def report_clones(changed):
    by_name = {project_relative(f): (f, lines) for f, lines in changed.items()}
    with tempfile.TemporaryDirectory() as out:
        subprocess.run(
            ["jscpd", "src", "tests", "--format", "csharp", "--min-tokens", "70", "--mode", "mild",
             "--reporters", "json", "--output", out, "--silent"],
            check=True, capture_output=True, cwd=ROOT)
        report = json.loads((Path(out) / "jscpd-report.json").read_text())

    def hit(fragment):
        entry = by_name.get(fragment["name"])
        return entry is not None and touches(entry[1], fragment["start"], fragment["end"])

    clones = [d for d in report["duplicates"] if hit(d["firstFile"]) or hit(d["secondFile"])]
    if clones:
        print(f"jscpd: {len(clones)} duplicated block(s) hold changed lines:")
        for d in clones:
            a, b = d["firstFile"], d["secondFile"]
            print(f"  {d['lines']} lines: {a['name']}:{a['start']}-{a['end']} and {b['name']}:{b['start']}-{b['end']}")


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


def report_metrics(changed):
    projects = set()
    for f in changed:
        for parent in (ROOT / f).parents:
            if list(parent.glob("*.csproj")):
                projects.add(parent)
                break
    found = []
    for project in sorted(projects):
        for sarif in project.glob("obj/Debug/*/analyzers.sarif"):
            for result in json.loads(sarif.read_text())["runs"][0].get("results", []):
                if result["ruleId"] not in RULES:
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
                    found.append(f"  {name}:{first} {RULES[result['ruleId']]}: {result['message']['text']}")
    if found:
        print(f"Complexity and length: {len(found)} changed method(s) are over a limit:")
        print("\n".join(sorted(set(found))))


def main():
    changed = changed_lines()
    if not changed:
        return
    for report in (report_clones, report_metrics):
        try:
            report(changed)
        except Exception as error:  # a report never stops the commit
            print(f"{report.__name__} failed: {error}", file=sys.stderr)


if __name__ == "__main__":
    main()
