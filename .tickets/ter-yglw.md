---
id: ter-yglw
status: in_progress
deps: []
links: []
created: 2026-11-22T21:13:04Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-6fka
tags: [rendering, experiment]
---
# Cost of one small environment face on the Mac's deck: 32 x 32 with small objects culled against today's 128 x 128

Experiment E3 of docs/research/lighting-system-review-2026-11-22/README.md, section 12. Time the existing environment face pass on the deck at a face size of 32 with a cull of objects below one texel, against today's face of 128. Design P needs about 216 faces for a cabin, so the face cost decides whether it fits.

## Acceptance Criteria

GPU time per face on the Mac and on the RTX 2000 Ada at both sizes, at least two runs each at a quiet load. Reading: above about 0.2 ms per face on the Mac with the cull, a refresh within 60 draws costs over 1.5 ms per draw, and design P does not fit as sketched.


## Notes

**2026-11-23T02:53:46Z**

2026-11-22, the line orchestrator. Launched as a continuation of the lead that made the ray census. Child thread: thread:delegated-task:command%3Amcp%3A49b05ffc-1378-4e84-92ef-b16c94dc7acb%3Adelegate-task%3Aslabs-4-ter-qt72-lead-1. taskId: node:delegated-task:command%3Amcp%3A4b9f2319-04a8-41d4-bf6b-a728beb5e565%3Athread-send%3Aslabs-4-continue-ter-yglw-face-cost. clientRequestId: slabs-4-continue-ter-yglw-face-cost. Target: claudeAgent, claude-opus-5-5, effort high. Worktree /Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a, branch t3/fix-projection-aspect-wobble at main 31fcc4299. No reviewer stage. Cloud money: 0.20 dollars. Definitions that I set in the brief: the face cost is the GPU time of everything that one face's refresh adds to a draw, with each counted pass named. The cull leaves an object out of a face when its bounding sphere covers less than one texel of that face from the capture point. Four variants separate the size from the cull: 128 and 32, each with and without the cull. Each card job starts with one discarded warm-up run (t1-80jd). The record goes to docs/research/environment-face-cost/.

**2026-11-23T02:56:25Z**

2026-11-22, the line orchestrator. Paused a few minutes after its launch: the orchestrator put ter-pdw8 ahead of it on the same lead. It follows after ter-pdw8 with the same brief.

**2026-11-23T06:21:09Z**

2026-11-23, the line orchestrator. Started again after ter-pdw8 closed, as a new continuation of the same lead, from main's head after Design X (65a24d189). Child thread: thread:delegated-task:command%3Amcp%3A49b05ffc-1378-4e84-92ef-b16c94dc7acb%3Adelegate-task%3Aslabs-4-ter-qt72-lead-1. taskId: node:delegated-task:command%3Amcp%3A971cdc95-fc79-4b4a-85b5-3a44eebfa7a8%3Athread-send%3Aslabs-4-continue-ter-yglw-face-cost-2. clientRequestId: slabs-4-continue-ter-yglw-face-cost-2. Target: claudeAgent, claude-opus-5-5, effort high. Worktree /Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a, branch t3/fix-projection-aspect-wobble. The brief's definitions are unchanged: the face cost, the cull by a bounding sphere below one texel, and the four variants. Added: the count of objects that the cull removes, the cost per draw of 216 faces within 60 draws per variant on both machines, and the part of a face's cost that does not shrink with its size. The probe never enters src; with the disk under 30 GiB free it is an uncommitted change in the lead's worktree, reverted before the final report. Cloud money: 0.20 dollars.
