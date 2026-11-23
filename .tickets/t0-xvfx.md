---
id: t0-xvfx
status: open
deps: []
links: [t2-x53j]
created: 2026-11-23T10:47:50Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [tools, rendering]
---
# The register guard lets a commit through without a count on a Mac whose dev shell has no decoder

Found by the orchestrator in the merged guard of t2-x53j (tools/shader_registers.py, applicability). When Xcode's metal-tt is present and the applegpu decoder is absent (APPLEGPU names no folder with applegpu.py), the guard prints "does not apply" and exits 3, and the pre-commit hook lets the commit go on. Inside a dev shell on a Mac with metal-tt, an absent APPLEGPU can only mean a shell that was entered before the guard's merge (the flake sets APPLEGPU since c9cd858bc). That machine is one where the guard does apply. So a commit that changes a file in a guarded shader's include closure goes through without a count, against the hook's own rule that only a pass and a true "does not apply" let a commit go on, because then no count passed. The class of the bug: a result that says "nothing to check" for a state in which the check could not run.

## Design

With metal-tt present and the decoder absent, the guard stops the commit: an error of the tool (exit 2) whose words tell the reader to enter the dev shell again (nix develop), because its APPLEGPU is not set. "Does not apply" (exit 3) stays for a machine without metal-tt, and for a commit that stages no file of a guarded closure. The decision lives in applicability, the one place that maps the machine's state to a result. tools/README.md states the rule.

## Acceptance Criteria

A test, red first, pins that metal-tt present with no decoder gives the stop with the words about the dev shell, and a second test pins that no metal-tt still gives "does not apply". tools/test_shader_registers.py passes. tools/README.md and the tool's own usage text say the same as the code.

