---
id: ter-xdt8
status: open
deps: []
links: []
created: 2026-10-18T23:50:45Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, physics]
---
# Rails motion for every entity: samples keyed by entity, MoveKinematic as a pose-only sample

User decision (2026-10-18): frames follow their owner entity, and the rails components move their entity, so rails motion must apply to any entity, not only to frame owners. Today Self.MoveOnRails refuses an entity that owns no frame, the sample table lives in the frame tree keyed by frame, and physics reads samples only for hulls (owner.OwnedFrame). Scope: (1) one motion sample per entity, keyed by entity with the generation check; a frame's step record reads its owner's sample; (2) every kinematic body with a sample follows it and ends its step exactly on it, hull or not; an entity without a body and without a frame has its transform row follow the sample; (3) MoveKinematic becomes a sample that gives only its end pose, so one intent drives every kinematic body, and physics works out the velocity at the step start as Jolt does now; (4) the docs. The design section goes into docs/plans/rails-components.md first, with the analogue of each new value, every reader and writer with the step it needs, and the flow per kind; open forks go to the user before any builder starts. ter-zg77 depends on this ticket and covers every fast kinematic body, not only hulls. Build it after ter-sn28 merges, because both change the frame tree and the sync point.

