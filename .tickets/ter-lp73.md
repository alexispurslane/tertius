---
id: ter-lp73
status: open
deps: []
links: []
created: 2026-10-24T21:07:56Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [tooling]
---
# Cleanup: Roslyn for every semantic fix, CSharpier for formatting, no ReSharper

The user decided on 2026-10-24 to remove ReSharper from the cleanup, move every semantic fix into Roslyn and add CSharpier as the formatter. ReSharper's C# front end is not the compiler and misreads C# 15: its cleanup turned named arguments positional, removed default-valued arguments, reordered static initializers (100 Host tests failed), rewrote a field initializer into a self-read (ChaseCamera), deleted empty default sections that IDE0010 needs, and deleted a using static that a test still uses. tools/cleanup.sh becomes: dotnet format style with the Roslyn rules (suggestion severity for fixes that the cleanup applies, warning where the build must fail), then CSharpier last, so that its formatting wins. Replace each ReSharper profile task by its Roslyn rule: redundancies (IDE0004, IDE0005, IDE0047 and the like), IDE0032 auto property, IDE0044 readonly field, IDE0005 usings, IDE0001/IDE0002 shorten names, IDE0003/IDE0009 this qualifiers, IDE0049 built-in type names. Member reordering and the get-only auto property fix have no Roslyn rule and are lost, unless Roslynator covers them. Turn off IDE0055 and every other Roslyn formatting rule that would fight CSharpier. Remove jetbrains.resharper.globaltools from the tool manifest, the cleanup profile and every resharper_* key that only the cleanup used. Reformat the whole codebase once with the new cleanup.

