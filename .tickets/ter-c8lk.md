---
id: ter-c8lk
status: open
deps: []
links: []
created: 2026-10-30T18:09:41Z
type: task
priority: 3
assignee: Alexis Purslane
---
# Keep event storage changes and event-type metadata under EventBuffer ownership

Found during ter-dyss qualitative calibration, baseline 0eb95afa. EventList.cs exposes EventBuffer.Items/Count/Mask/Version and repeats structural bookkeeping during append/removal (about lines 195-248). Writable whole-event references can change Event.Type without updating the mask used by World.Tick to skip components; restriction is documented but not enforced. No current caller violating it was demonstrated. Move complete structural changes into EventBuffer and design typed payload mutation/inspection so event-type replacement cannot bypass mask maintenance. Preserve reusable buffer vs short-lived ref-struct view, allocation-free steady state, and ordering. Reviewer evidence at /tmp/tertius-class-quality-sample-review.md (sections1-2); essential facts retained here.

## Acceptance Criteria

Append, indexed removal, and supported type replacement preserve count, mask, and enumeration version through one owner. Tests demonstrate scheduler visibility after supported changes and safe typed payload mutation; preserve steady-state allocation behavior. Review existing ref Event callers in Carry and BoxPlay; update ARCHITECTURE.md.

