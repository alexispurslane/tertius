---
id: ter-fc0y
status: open
deps: []
links: []
created: 2026-10-31T22:26:13Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [tests]
---
# Assertion failure messages name the wrong subject in the Checked configuration

Found in ter-kr7e (2026-10-31). Checked compiles the tests with Optimize=true (Directory.Build.targets:27). AwesomeAssertions finds the subject of a failed assertion by reading the source line of the calling stack frame. With optimized code, that line can be off by a statement. Measured: with the ter-kr7e mutant applied, HairRenderTests.AHairCardLitFromBehindKeepsTheShadowOfACasterCloseBehindIt reports 'Expected lit.X to be less than or equal to 0.0018641353F' in Checked, but 'Expected leak to be ...' in Debug. The values in the message are correct in both. The work: choose a project-wide remedy, for example full debug line information for the test projects in Checked, or a setting that turns off caller identification. Make sure that a failing test in Checked names its own subject.

