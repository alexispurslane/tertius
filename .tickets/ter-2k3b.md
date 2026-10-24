---
id: ter-2k3b
status: open
deps: []
links: []
created: 2026-10-24T17:07:50Z
type: task
priority: 2
assignee: Alexis Purslane
---
# ScriptCommand.Options: a union of free play and a script run

ScriptCommand.Run tests its options at the outer shell to tell free play from a script run (ter-2ku2 added the test to remove the ! operator). Make Options a union of the two cases, so that each case holds only its own fields. ScriptCommand.Run is also the sixth heaviest function by erosion mass (CC 27, 121 lines), so split it per case. Found by the style analyzer work.

