---
id: ter-ziot
status: open
deps: []
links: []
created: 2026-10-24T19:35:51Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [analyzers, docs]
---
# Style analyzer: the doc comment word limits

Add a rule to analyzers/Tertius.Style that enforces the doc comment limits of STYLE.md, "Comments", in src and tools: at most 50 words in the summary and remarks together, at most 20 words in each param, returns and typeparam, and at most 25 words in each item of a type's invariant list, which does not count toward the 50. A word is a run of text between spaces; a cref or a code element counts as one word. The rule reports the count and the limit. Tests and hand mutations as for the other TS rules. It goes after the strip pass, so that it starts with no hits.


## Notes

**2026-10-24T19:39:05Z**

The user approved the doc comment rules of STYLE.md, "Comments", on 2026-10-24: 50 words for summary and remarks, 20 per param, returns and typeparam, 25 per invariant item outside the 50.

**2026-10-24T21:00:29Z**

Changed by the user on 2026-10-24: a hit of this rule must never fail the build. Give it the severity Info (DiagnosticSeverity.Info, suggestion in .editorconfig), so that the IDE and csharp-ls show it, but TreatWarningsAsErrors does not make it an error. It no longer waits for the strip pass, which is on hold, so it starts with hits. Report the hit count instead of fixing them.
