---
id: ter-nx38
status: open
deps: []
links: []
created: 2026-10-19T19:27:21Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [performance, frames]
---
# The rails sync point's last 39 us: the field of the proper acceleration rule and the motion intent swap

Follow-up of ter-6azq (2026-10-19). After ter-6azq, with 1,000 Kepler frames on one thread, the sync point is 153.7 us against 114.9 us before the motion intent. Two parts are left, each from a user decision in docs/plans/rails-components.md: (1) the field at each frame's place for the derived proper acceleration, about 19 us (19 ns per frame, which looks high for one square root and one division, so first look at the loads around it); (2) step 4 clears the 384-byte front intent of each entity, because the motion intent lives inside the entity's intent, about 8 us, plus 4 us to strip and 4 us of bookkeeping. Options that stay inside the design: look closer at (1), run the step 2 pass on the workers above a frame count, clear old front entries lazily. Options that reverse a decision: a rails component delivers its frame's proper acceleration, or motion intents get a dense store of their own. The user was told on 2026-10-19 and has not asked for either reversal; the orchestrator recommended accepting the gap.

