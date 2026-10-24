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


## Notes

**2026-10-24T22:00:29Z**

Built on branch ter-lp73-roslyn-csharpier: tooling commit a3350dd2, reformat commit 93fbc0b4. Decisions: every IDE rule with a fix that agrees with STYLE.md is on in .editorconfig, with its reason. IDE0004 is off, because Roslyn 5.11 takes a cast to a C# 15 union for redundant (2 build errors, 3 failed tests). IDE0042, IDE0048, IDE0058, IDE0079, IDE0130 and the formatting rules IDE0055 and IDE2000-IDE2006 are off. IDE0033 and IDE0059 are new warnings. Roslynator.Analyzers 5.0.1 stays, for 13 rules, among them RCS1170 (get-only auto property); RCS1123 is off (it parenthesizes a + b * c); the roslynator CLI is out (its own Roslyn 5.0 cannot parse C# 15). CSharpier 1.3.0 from nixpkgs, printWidth 120; .csharpierignore holds PhysicsSeam.cs, CaptureTarget.cs, Script.cs and ScriptRunner.cs. Member ordering has no rule and is now a STYLE.md rule for people. The cleanup builds the union generator in Debug first, because dotnet format loads that build. Timings: 1 file 20 s clean, 33 s with a fix (ReSharper: 32 s); 20 files in 10 projects 36 s clean, 64 s with a fix; --all 70 s clean, about 3 min for the first reformat (ReSharper: about 5 min). A second --all after the reformat changed nothing. Tests: Checked 7092 (7080 passed, 12 skipped), Release 7080 (7068 passed, 12 skipped), 0 failed.
