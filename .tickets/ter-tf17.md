---
id: ter-tf17
status: closed
deps: []
links: []
created: 2026-10-27T17:20:30Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
---
# The commit report shows the erosion and the verbosity, with the touched code that raises each

The user's request (2026-10-27): show the project-wide erosion and verbosity in the pre-commit report, each with the code that the commit touched and that raises it, so that a person or an agent that commits gets feedback it can act on.

Today the pre-commit hook measures both metrics of the staged tree (`Tertius.Slop measure`, step 4) but prints neither. The pre-push hook prints them per push. The report of step 2 lists methods over CA1502's complexity limit of 15 and jscpd blocks on changed lines.

The report gets a section for each metric:

- Erosion: the value of the staged tree, and its change against the parent commit's note in refs/notes/slop when the parent has one. Under it, each function with a changed line and a CC above 10 (the erosion's own CC and threshold), with its file, line, CC, SLOC and mass, the greatest mass first, and a line that says that a split of such a function lowers the erosion.
- Verbosity: the value and its change, the same way. Under it, each finding of a verbosity rule that covers a changed line, with the rule's message (what to write instead), and each clone that covers a changed line, with both of its places.

Tertius.Slop prints only the top 10 functions and the top 20 findings today. It needs to give every function with a CC above 10 and every finding and clone, or to take the changed lines from the hook and filter them itself.

## Design

Open fork for the user, at the design: the report then holds two measures of complexity with different counts and limits, CA1502 (Microsoft's CC, limit 15) and the erosion (Slop's CC, limit 10), and two lists of clones on changed lines. By the software-design skill's rule against a parallel mechanism, the erosion and verbosity sections replace the CA1502 list and the jscpd list, and CA1502 goes off. That changes the limit that a commit sees from 15 to 10, so the user decides. The MA0051 length list stays, because neither metric measures length. Test code keeps the length list only, because Slop measures only src.

The measure runs in parallel with the tests, so the two sections print after it ends. A commit with no C# change prints neither, as today.

## Acceptance Criteria

- A commit that touches a function with a CC above 10 sees it under the erosion, with the values above.
- A commit that adds a flagged line or a clone sees it under the verbosity, with the rule's message or the clone's other place.
- tools/README.md ("Before a commit", "Slop metrics") and docs/plans/code-quality-tooling.md say the new report.
- The hook's own tests (if any) and Tertius.Slop.Tests cover the new output.


## Notes

**2026-10-27T18:01:04Z**

The user's answer (2026-10-27): replace them. The erosion and verbosity sections replace the CA1502 list and the jscpd list of the commit report, CA1502 goes off, and the MA0051 length list stays.
