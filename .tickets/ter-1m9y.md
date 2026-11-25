---
id: ter-1m9y
status: in_progress
deps: [ter-tiei]
links: []
created: 2026-11-25T15:25:33Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-qdn8
---
# Animate the suited pilot walking, sitting and taking the pencil

Use the real rig and existing animation/deformation and attachment systems. Match foot contact and hand-to-pencil contact to the simulated movement and events.


The moving elevator owns its physical floor contact. Check the rig's boot soles against that floor on entry, cruise and exit. Keep the car's physics correction out of the rig pose.

## Notes

**2026-11-25T17:07:12Z**

The pilot worktree starts from the fitted shuttle at 48bc3b613. T3 task and thread suffix ter-1m9y-pilot-on-fitted-cabin (command 5ea2b7ce-a373-47af-ac8e-898f43b2d29c). The parent will check boot contact on the combined berth and elevator.
