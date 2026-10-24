---
id: ter-jjdv
status: open
deps: [ter-hahu]
links: []
created: 2026-10-24T19:35:51Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Survey: other types that allow illegal states

Find the other places in src where a type allows an illegal state, by the signs in STYLE.md, "Make illegal states unrepresentable": a Kind enum beside fields that only some kinds use, a nullable field guarded by [MemberNotNullWhen] or a contract, a sentinel such as -1 or an Invalid id beside data valid only without it, parallel arrays whose entries mean something only in some rows, and options that allow a combination that the code refuses. BodyRecord, ScriptCommand.Options, the numeric frame arrays of FrameTree and the Kepler attitude have tickets already. Report each place with a union sketch, create one ticket per place, and say which signs a machine can flag, as a candidate rule for the style analyzer or the slop tool.

