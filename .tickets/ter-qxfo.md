---
id: ter-qxfo
status: open
deps: [ter-ghts, ter-hahu]
links: []
created: 2026-10-24T19:35:51Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [docs]
---
# Doc comments: strip every one to the STYLE.md limits

Shorten every doc comment in src, analyzers and tools to the limits of STYLE.md, "Comments": at most 50 words of summary and remarks, at most 20 words per param, returns and typeparam, and a type's invariant list outside the 50. A doc comment keeps what the member is or does, its invariants, and a reason that belongs to the member alone, in one sentence. The model and the reasons that span members move to ARCHITECTURE.md or a design doc, and the comment points to the section.

Today: 3,541 doc comments in src hold 175,633 words. The median is 22 words, and 896 comments are over 50 words. The cut at 50 words removes about 78,000 words, 44 percent of the total.

Process:
1. The orchestrator runs one Haiku 5.5 agent at medium effort per module (Core, Simulation, Physics, Rendering, Host, Demo, and one for analyzers plus tools), each in its own worktree, in parallel. Each agent changes only doc comments in its own module, never code and never ARCHITECTURE.md or docs/plans.
2. For each fact that an agent cuts, the agent searches ARCHITECTURE.md and docs/plans. If the fact is there, the comment points to that section. If the fact is nowhere else, the agent lists it in its report with the file, the member, the full text of the fact and the doc section that it proposes. An invariant never leaves the doc comment.
3. Each agent builds its module, so that every cref still resolves, and reports the word count before and after.
4. The orchestrator moves the facts that the reports list into ARCHITECTURE.md or the design docs, with one home per fact.
5. One Sonnet agent at medium effort per engine module (Core, Simulation, Physics, Rendering) reviews the diff for a lost invariant and a lost fact that is in no doc, and fixes what it finds. The orchestrator reviews Host, Demo, analyzers and tools itself.
6. Merge, run tools/test-all.sh, and report the new word counts.

Run it after the second batch of style rules merges, because that batch touches files in every module.


## Notes

**2026-10-24T19:39:05Z**

The user approved the doc comment rules of STYLE.md, "Comments", on 2026-10-24: 50 words for summary and remarks, 20 per param, returns and typeparam, 25 per invariant item outside the 50.

**2026-10-24T21:00:29Z**

On hold: the user said on 2026-10-24 to leave the doc comments alone for now. Do not start this pass until the user asks for it.
