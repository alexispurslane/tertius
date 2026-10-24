---
id: ter-71kd
status: open
deps: []
links: []
created: 2026-10-24T17:07:50Z
type: task
priority: 2
assignee: Alexis Purslane
---
# BodyRecord: its primary state as a union

BodyRecord holds nullable World and Character fields whose presence the physics backend guards with contracts and [MemberNotNullWhen] (ter-2ku2 added them to remove the ! operator). Make the primary state a union of its cases, so that the type says which fields exist and the contracts go away. Found by the style analyzer work.

