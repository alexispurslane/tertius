---
id: ter-7e55
status: open
deps: [ter-se84]
links: []
created: 2026-10-11T13:52:11Z
type: epic
priority: 2
assignee: Alexis Purslane
tags: [tests]
---
# Test hardening: coverage, mutation testing and a soak run

Design: docs/plans/test-hardening.md. The vertical slice shows what is right, so this locks in verified behavior.


## Notes

**2026-10-21T18:42:20Z**

From ter-ftk4 (2026-10-21): the Release path of Attachments.CarryPushes for a push whose frame differs from its root's front row has no test. A test can reach it through an internal edit of the front intent, and must see the push dropped and the line in World.Errors. Also run Stryker on the changed lines of Attachments.cs and EntityTable.cs from that merge.
